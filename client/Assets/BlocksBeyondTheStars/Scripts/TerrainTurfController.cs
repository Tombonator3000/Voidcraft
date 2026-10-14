// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws a short, interactive shell-turf layer on nearby grass TOP faces without changing chunk meshes or
    /// collision. A shared seeded biome mask paints sparse earth patches on the base grass and thins the lifted
    /// shells above exactly the same places, so vegetation dissolves into soil instead of ending at a hard edge.
    ///
    /// The mask/wind/trampling approach is an original Unity/HLSL adaptation inspired by Christian Ortiz'
    /// MIT-licensed stylized-components grass study; no Three.js/React source or assets are imported.
    /// </summary>
    public sealed class TerrainTurfController : MonoBehaviour
    {
        private static readonly int GrassUvId = Shader.PropertyToID("_GrassUvRect");
        private static readonly int LayerIndexId = Shader.PropertyToID("_LayerIndex");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int ShellHeightId = Shader.PropertyToID("_ShellHeight");
        private static readonly int TurfTimeId = Shader.PropertyToID("_TurfTime");
        private static readonly int WindId = Shader.PropertyToID("_WindStrength");
        private static readonly int BiomeSeedId = Shader.PropertyToID("_BiomeSeed");
        private static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
        private static readonly int TrampleRadiusId = Shader.PropertyToID("_TrampleRadius");
        private static readonly int DirtStrengthId = Shader.PropertyToID("_DirtStrength");

        private static TerrainTurfController _instance;

        private sealed class ChunkDraw
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
        }

        private readonly List<ChunkDraw> _chunks = new List<ChunkDraw>(256);
        private readonly List<MeshRenderer> _scan = new List<MeshRenderer>(512);
        private readonly List<Material> _layers = new List<Material>(5);

        private GameBootstrap _game;
        private Camera _camera;
        private Shader _shader;
        private Texture _atlasTexture;
        private QualityPreset _preset = (QualityPreset)(-1);
        private bool _reduced;
        private bool _active;
        private float _range;
        private float _trampleRadius;
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

            int shellCount = preset == QualityPreset.High ? 4 : 2;
            float height = preset == QualityPreset.High ? 0.13f : 0.070f;
            _range = preset == QualityPreset.High ? 25f : 15f;
            _trampleRadius = preset == QualityPreset.High ? 1.35f : 1.05f;

            // Layer zero is flush with the block surface. It uses exactly the same biome mask as the lifted
            // shells and softly earth-tints the holes they leave, giving one coherent grass -> dirt transition.
            for (int i = 0; i <= shellCount; i++)
            {
                var mat = new Material(_shader)
                {
                    name = i == 0 ? "TerrainTurf_GroundMask" : $"TerrainTurf_{i}of{shellCount}",
                    mainTexture = atlas,
                    renderQueue = 3000,
                };
                mat.SetVector(GrassUvId, uvRect);
                mat.SetFloat(LayerIndexId, i);
                mat.SetFloat(LayerCountId, shellCount);
                mat.SetFloat(ShellHeightId, height);
                mat.SetFloat(DirtStrengthId, preset == QualityPreset.High ? 0.82f : 0.68f);
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
            float seed = StableSeed01(_game.WorldSeed, _game.LocationName);
            Vector3 player = _game.PlayerPosition;
            foreach (var mat in _layers)
            {
                mat.SetFloat(TurfTimeId, time);
                mat.SetFloat(WindId, wind);
                mat.SetFloat(BiomeSeedId, seed);
                mat.SetVector(PlayerPosId, new Vector4(player.x, player.y, player.z, 1f));
                mat.SetFloat(TrampleRadiusId, _trampleRadius);
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
                    // Submesh 0 is opaque terrain. Transparent water/glass and player paint never pay this overdraw.
                    Graphics.DrawMesh(mesh, matrix, mat, layer, _camera, 0);
                }
            }
        }

        private static float StableSeed01(long worldSeed, string location)
        {
            unchecked
            {
                ulong h = (ulong)worldSeed ^ 1469598103934665603UL;
                foreach (char c in location ?? string.Empty)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }

                return (h & 0xFFFFFFUL) / 16777215f;
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
