// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws a short shell-turf layer on nearby grass TOP faces without changing chunk meshes or collision.
    ///
    /// The controller deliberately re-draws only submesh 0 (opaque terrain) through a tiny clip shader. The shader
    /// rejects every atlas tile except grass and every face except world-up tops, so transparent/paint submeshes,
    /// ships and arbitrary scene meshes never receive turf. Medium uses two shells; High uses four. Low/Potato,
    /// Reduced Effects, Built-in RP and space flight stay on the original voxel surface only.
    /// </summary>
    public sealed class TerrainTurfController : MonoBehaviour
    {
        private static readonly int GrassUvId = Shader.PropertyToID("_GrassUvRect");
        private static readonly int LayerIndexId = Shader.PropertyToID("_LayerIndex");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int ShellHeightId = Shader.PropertyToID("_ShellHeight");
        private static readonly int TurfTimeId = Shader.PropertyToID("_TurfTime");
        private static readonly int WindId = Shader.PropertyToID("_WindStrength");

        private static TerrainTurfController _instance;

        private sealed class ChunkDraw
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
        }

        private readonly List<ChunkDraw> _chunks = new List<ChunkDraw>(256);
        private readonly List<MeshRenderer> _scan = new List<MeshRenderer>(512);
        private readonly List<Material> _layers = new List<Material>(4);

        private GameBootstrap _game;
        private Camera _camera;
        private Shader _shader;
        private Texture _atlasTexture;
        private QualityPreset _preset = (QualityPreset)(-1);
        private bool _reduced;
        private bool _active;
        private float _range;
        private float _nextSetup;
        private float _nextChunkScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject("TerrainTurfController");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<TerrainTurfController>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextSetup)
            {
                return;
            }

            _nextSetup = Time.unscaledTime + 0.25f;

            if (_game == null)
            {
                _game = FindFirstObjectByType<GameBootstrap>();
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_shader == null)
            {
                _shader = Resources.Load<Shader>("TerrainTurf") ?? Shader.Find("BlocksBeyondTheStars/TerrainTurf");
            }

            var settings = _game?.Settings;
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            bool canDraw = _game != null && _camera != null && _shader != null && _game.Atlas != null
                           && settings != null && urp && !settings.ReducedEffects && !_game.SpaceViewActive
                           && (settings.Preset == QualityPreset.Medium || settings.Preset == QualityPreset.High);

            if (!canDraw)
            {
                _active = false;
                return;
            }

            Texture atlas = _game.Atlas.Texture;
            bool changed = !_active || settings.Preset != _preset || settings.ReducedEffects != _reduced || atlas != _atlasTexture;
            if (changed)
            {
                RebuildLayers(settings.Preset, atlas);
                _preset = settings.Preset;
                _reduced = settings.ReducedEffects;
                _atlasTexture = atlas;
            }

            _active = _layers.Count > 0;

            if (_active && Time.unscaledTime >= _nextChunkScan)
            {
                _nextChunkScan = Time.unscaledTime + 0.75f;
                ScanChunks();
            }
        }

        private void RebuildLayers(QualityPreset preset, Texture atlas)
        {
            DestroyLayers();
            if (_game?.Content == null || _game.Atlas == null || _shader == null || atlas == null)
            {
                return;
            }

            var grass = _game.Content.GetBlock("grass");
            if (grass == null || grass.NumericId.Value == 0)
            {
                return;
            }

            Rect uv = _game.Atlas.TileUv(grass.NumericId.Value);
            Vector4 uvRect = new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax);

            int count = preset == QualityPreset.High ? 4 : 2;
            float height = preset == QualityPreset.High ? 0.12f : 0.065f;
            _range = preset == QualityPreset.High ? 24f : 14f;

            for (int i = 1; i <= count; i++)
            {
                var mat = new Material(_shader)
                {
                    name = $"TerrainTurf_{i}of{count}",
                    mainTexture = atlas,
                    renderQueue = 3000,
                };
                mat.SetVector(GrassUvId, uvRect);
                mat.SetFloat(LayerIndexId, i);
                mat.SetFloat(LayerCountId, count);
                mat.SetFloat(ShellHeightId, height);
                _layers.Add(mat);
            }
        }

        private void ScanChunks()
        {
            _chunks.Clear();
            _scan.Clear();
            if (_game == null)
            {
                return;
            }

            _game.GetComponentsInChildren<MeshRenderer>(false, _scan);
            foreach (var renderer in _scan)
            {
                if (renderer == null || renderer.transform.parent != _game.transform
                    || !renderer.gameObject.name.StartsWith("Chunk ", StringComparison.Ordinal))
                {
                    continue;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    _chunks.Add(new ChunkDraw { Renderer = renderer, Filter = filter });
                }
            }
        }

        private void LateUpdate()
        {
            if (!_active || _camera == null || _game == null || _game.SpaceViewActive || _layers.Count == 0)
            {
                return;
            }

            float time = _game.WorldTime;
            float wind = Mathf.Clamp01(_game.WindSpeed);
            foreach (var mat in _layers)
            {
                mat.SetFloat(TurfTimeId, time);
                mat.SetFloat(WindId, wind);
            }

            Vector3 cameraPos = _camera.transform.position;
            float rangeSq = _range * _range;
            foreach (var chunk in _chunks)
            {
                if (chunk?.Renderer == null || chunk.Filter == null || !chunk.Renderer.enabled
                    || !chunk.Renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Mesh mesh = chunk.Filter.sharedMesh;
                if (mesh == null || mesh.subMeshCount < 1 || chunk.Renderer.bounds.SqrDistance(cameraPos) > rangeSq)
                {
                    continue;
                }

                Matrix4x4 matrix = chunk.Renderer.localToWorldMatrix;
                int layer = chunk.Renderer.gameObject.layer;
                foreach (var mat in _layers)
                {
                    // Submesh 0 is the opaque block terrain. Transparent glass/water and player-painted faces live
                    // in later submeshes and therefore never pay the turf overdraw.
                    Graphics.DrawMesh(mesh, matrix, mat, layer, _camera, 0);
                }
            }
        }

        private void DestroyLayers()
        {
            foreach (var mat in _layers)
            {
                if (mat != null)
                {
                    Destroy(mat);
                }
            }

            _layers.Clear();
        }

        private void OnDestroy()
        {
            DestroyLayers();
            _chunks.Clear();
            _scan.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
