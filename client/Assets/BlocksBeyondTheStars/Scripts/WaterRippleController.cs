// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Presentation-only interactive water rings. Uses the rendered client world to determine when the local
    /// player's feet touch water, keeps a tiny bounded ring history, and redraws only nearby transparent terrain
    /// through an additive water-only overlay. The existing water shader remains responsible for depth/refraction/SSR.
    /// Inspired by Christian Ortiz' MIT stylized-components water study; this is original Unity C#/HLSL.
    /// </summary>
    public sealed class WaterRippleController : MonoBehaviour
    {
        private const int MaxRipples = 8;
        private static readonly int RippleCountId = Shader.PropertyToID("_RippleCount");
        private static readonly int RipplesId = Shader.PropertyToID("_Ripples");
        private static readonly int StrengthsId = Shader.PropertyToID("_RippleStrengths");
        private static readonly int RippleTimeId = Shader.PropertyToID("_RippleTime");
        private static WaterRippleController _instance;

        private sealed class ChunkDraw { public MeshRenderer Renderer; public MeshFilter Filter; }
        private struct Ripple { public Vector3 Center; public float Start; public float Strength; }

        private readonly List<ChunkDraw> _chunks = new List<ChunkDraw>(256);
        private readonly List<MeshRenderer> _scan = new List<MeshRenderer>(512);
        private readonly Ripple[] _history = new Ripple[MaxRipples];
        private readonly Vector4[] _packed = new Vector4[MaxRipples];
        private readonly float[] _strengths = new float[MaxRipples];

        private GameBootstrap _game;
        private Camera _camera;
        private Material _material;
        private Shader _shader;
        private ushort _waterId;
        private int _writeIndex;
        private int _rippleCount;
        private bool _wasInWater;
        private float _lastSurfaceY;
        private Vector3 _lastPlayerPos;
        private bool _haveLastPos;
        private float _lastSampleRealtime;
        private float _lastMoveEmit = -100f;
        private float _nextSetup;
        private float _nextScan;
        private bool _active;
        private float _range;
        private int _qualityLimit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var host = new GameObject("WaterRippleController");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<WaterRippleController>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextSetup) return;
            _nextSetup = Time.unscaledTime + 0.15f;

            if (_game == null)
            {
                _game = FindFirstObjectByType<GameBootstrap>();
                _waterId = 0;
                _haveLastPos = false;
            }
            if (_camera == null) _camera = Camera.main;
            if (_shader == null)
                _shader = Resources.Load<Shader>("WaterInteraction") ?? Shader.Find("BlocksBeyondTheStars/WaterInteraction");
            if (_waterId == 0 && _game?.Content?.GetBlock("water") is { } water)
                _waterId = water.NumericId.Value;

            var settings = _game?.Settings;
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            bool wanted = _game != null && _camera != null && _shader != null && _waterId != 0
                          && settings != null && urp && !settings.ReducedEffects && !_game.SpaceViewActive
                          && (settings.Preset == QualityPreset.Medium || settings.Preset == QualityPreset.High);
            if (!wanted)
            {
                _active = false;
                _wasInWater = false;
                _haveLastPos = false;
                return;
            }

            if (_material == null)
                _material = new Material(_shader) { name = "WaterInteraction", renderQueue = 3100 };

            _range = settings.Preset == QualityPreset.High ? 36f : 25f;
            _qualityLimit = settings.Preset == QualityPreset.High ? MaxRipples : 5;
            _active = true;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.85f;
                ScanChunks();
            }
            UpdateRippleEvents();
        }

        private void UpdateRippleEvents()
        {
            if (_game?.World == null) return;

            Vector3 p = _game.PlayerPosition;
            float now = _game.WorldTime;
            float realtime = Time.unscaledTime;
            float speed = 0f;
            if (_haveLastPos)
            {
                float dt = Mathf.Max(0.001f, realtime - _lastSampleRealtime);
                Vector2 delta = new Vector2(p.x - _lastPlayerPos.x, p.z - _lastPlayerPos.z);
                speed = delta.magnitude / dt;
            }
            _lastPlayerPos = p;
            _lastSampleRealtime = realtime;
            _haveLastPos = true;

            bool inWater = TryFindSurface(p, out float surfaceY);
            if (inWater)
            {
                _lastSurfaceY = surfaceY;
                if (!_wasInWater)
                {
                    Emit(new Vector3(p.x, surfaceY, p.z), now, 1f);
                    _lastMoveEmit = now;
                }
                else if (speed > 0.22f && now - _lastMoveEmit >= Mathf.Lerp(0.34f, 0.16f, Mathf.Clamp01(speed / 6f)))
                {
                    Emit(new Vector3(p.x, surfaceY, p.z), now, Mathf.Lerp(0.30f, 0.78f, Mathf.Clamp01(speed / 6f)));
                    _lastMoveEmit = now;
                }
            }
            else if (_wasInWater)
            {
                Emit(new Vector3(p.x, _lastSurfaceY, p.z), now, 0.72f);
            }
            _wasInWater = inWater;
        }

        private bool TryFindSurface(Vector3 player, out float surfaceY)
        {
            surfaceY = player.y;
            int x = Mathf.FloorToInt(player.x);
            int z = Mathf.FloorToInt(player.z);
            int baseY = Mathf.FloorToInt(player.y + 0.1f);
            int waterY = int.MinValue;
            for (int y = baseY - 1; y <= baseY + 1; y++)
            {
                if (_game.World.GetBlock(x, y, z).Value == _waterId) { waterY = y; break; }
            }
            if (waterY == int.MinValue) return false;

            int top = waterY;
            for (int y = waterY + 1; y <= waterY + 12; y++)
            {
                if (_game.World.GetBlock(x, y, z).Value != _waterId) break;
                top = y;
            }
            surfaceY = top + 1f;
            return true;
        }

        private void Emit(Vector3 center, float start, float strength)
        {
            _history[_writeIndex] = new Ripple { Center = center, Start = start, Strength = strength };
            _writeIndex = (_writeIndex + 1) % MaxRipples;
            _rippleCount = Mathf.Min(_rippleCount + 1, MaxRipples);
        }

        private void ScanChunks()
        {
            _chunks.Clear();
            _scan.Clear();
            _game.GetComponentsInChildren<MeshRenderer>(false, _scan);
            foreach (var renderer in _scan)
            {
                if (renderer == null || renderer.transform.parent != _game.transform
                    || !renderer.gameObject.name.StartsWith("Chunk ", StringComparison.Ordinal)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) _chunks.Add(new ChunkDraw { Renderer = renderer, Filter = filter });
            }
        }

        private void LateUpdate()
        {
            if (!_active || _game == null || _camera == null || _material == null || _game.SpaceViewActive) return;

            int count = Mathf.Min(_rippleCount, _qualityLimit);
            for (int i = 0; i < MaxRipples; i++) { _packed[i] = Vector4.zero; _strengths[i] = 0f; }
            for (int i = 0; i < count; i++)
            {
                int index = (_writeIndex - 1 - i + MaxRipples) % MaxRipples;
                Ripple r = _history[index];
                _packed[i] = new Vector4(r.Center.x, r.Center.y, r.Center.z, r.Start);
                _strengths[i] = r.Strength;
            }

            _material.SetInt(RippleCountId, count);
            _material.SetVectorArray(RipplesId, _packed);
            _material.SetFloatArray(StrengthsId, _strengths);
            _material.SetFloat(RippleTimeId, _game.WorldTime);

            Vector3 cameraPos = _camera.transform.position;
            float rangeSq = _range * _range;
            foreach (var chunk in _chunks)
            {
                if (chunk?.Renderer == null || chunk.Filter == null || !chunk.Renderer.enabled
                    || !chunk.Renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = chunk.Filter.sharedMesh;
                if (mesh == null || mesh.subMeshCount < 2 || chunk.Renderer.bounds.SqrDistance(cameraPos) > rangeSq) continue;
                Graphics.DrawMesh(mesh, chunk.Renderer.localToWorldMatrix, _material,
                    chunk.Renderer.gameObject.layer, _camera, 1);
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            _chunks.Clear();
            _scan.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
