// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// High-quality local water simulation: a bounded 256x256 height/velocity field around the player, advanced
    /// at 30 Hz on the GPU with a damped 2-D wave equation. Contact with actual water voxels injects impulses;
    /// the result is re-projected only onto nearby top-facing water faces as a subtle moving sheen.
    ///
    /// This is intentionally local and presentation-only. The existing voxel water shader remains responsible for
    /// geometry waves, depth, shoreline foam, refraction and SSR. The local simulation adds propagated interaction
    /// waves without changing blocks, collision, networking or server authority.
    /// </summary>
    public sealed class WaterWaveSimulationController : MonoBehaviour
    {
        private const int Resolution = 256;
        private const float WorldSize = 32f;
        private const float RecenterStep = 8f;
        private const float FixedStep = 1f / 30f;
        private const int MaxStepsPerFrame = 2;

        private static readonly int InjectUvId = Shader.PropertyToID("_InjectUv");
        private static readonly int InjectStrengthId = Shader.PropertyToID("_InjectStrength");
        private static readonly int InjectRadiusId = Shader.PropertyToID("_InjectRadius");
        private static readonly int WaveTexId = Shader.PropertyToID("_WaveTex");
        private static readonly int WaveCenterSizeId = Shader.PropertyToID("_WaveCenterSize");
        private static readonly int WaveSurfaceYId = Shader.PropertyToID("_WaveSurfaceY");
        private static WaterWaveSimulationController _instance;

        private sealed class ChunkDraw
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
        }

        private readonly List<ChunkDraw> _chunks = new List<ChunkDraw>(256);
        private readonly List<MeshRenderer> _scan = new List<MeshRenderer>(512);

        private GameBootstrap _game;
        private Camera _camera;
        private Material _updateMaterial;
        private Material _displayMaterial;
        private RenderTexture _stateA;
        private RenderTexture _stateB;
        private bool _aIsCurrent = true;
        private ushort _waterId;
        private bool _active;
        private bool _wasInWater;
        private bool _havePlayerPos;
        private Vector3 _lastPlayerPos;
        private float _lastSampleRealtime;
        private float _lastImpulseRealtime = -100f;
        private float _pendingImpulse;
        private Vector2 _pendingWorldXZ;
        private Vector2 _center;
        private bool _haveCenter;
        private float _surfaceY;
        private bool _haveSurface;
        private float _accumulator;
        private float _nextSetup;
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var host = new GameObject("WaterWaveSimulationController");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<WaterWaveSimulationController>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextSetup)
            {
                _nextSetup = Time.unscaledTime + 0.25f;
                ResolveAndGate();
            }

            if (!_active || _game == null || _camera == null) return;

            TrackWaterContact();
            UpdateSimulationCenter();

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.9f;
                ScanChunks();
            }

            _accumulator = Mathf.Min(_accumulator + Time.unscaledDeltaTime, FixedStep * MaxStepsPerFrame);
            int steps = 0;
            while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
            {
                StepSimulation();
                _accumulator -= FixedStep;
                steps++;
            }
        }

        private void ResolveAndGate()
        {
            if (_game == null)
            {
                _game = FindFirstObjectByType<GameBootstrap>();
                _waterId = 0;
                _havePlayerPos = false;
                _haveCenter = false;
                _haveSurface = false;
            }
            if (_camera == null) _camera = Camera.main;
            if (_waterId == 0 && _game?.Content?.GetBlock("water") is { } water)
                _waterId = water.NumericId.Value;

            var settings = _game?.Settings;
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            bool wanted = _game != null && _camera != null && _waterId != 0 && settings != null && urp
                          && settings.Preset == QualityPreset.High && !settings.ReducedEffects && !_game.SpaceViewActive;
            if (!wanted)
            {
                _active = false;
                _wasInWater = false;
                _havePlayerPos = false;
                return;
            }

            if (_updateMaterial == null || _displayMaterial == null || _stateA == null || _stateB == null)
            {
                if (!CreateResources())
                {
                    _active = false;
                    return;
                }
            }
            _active = true;
        }

        private bool CreateResources()
        {
            DestroyResources();
            Shader update = Resources.Load<Shader>("WaterWaveUpdate") ?? Shader.Find("Hidden/BlocksBeyondTheStars/WaterWaveUpdate");
            Shader display = Resources.Load<Shader>("WaterWaveDisplay") ?? Shader.Find("BlocksBeyondTheStars/WaterWaveDisplay");
            if (update == null || display == null) return false;

            _updateMaterial = new Material(update) { name = "WaterWaveUpdate" };
            _displayMaterial = new Material(display) { name = "WaterWaveDisplay", renderQueue = 3090 };
            _stateA = CreateStateTexture("WaterWaveStateA");
            _stateB = CreateStateTexture("WaterWaveStateB");
            ClearState();
            return _stateA != null && _stateB != null;
        }

        private static RenderTexture CreateStateTexture(string name)
        {
            var rt = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            rt.Create();
            return rt;
        }

        private void TrackWaterContact()
        {
            if (_game?.World == null) return;
            Vector3 p = _game.PlayerPosition;
            float realtime = Time.unscaledTime;
            float speed = 0f;
            if (_havePlayerPos)
            {
                float dt = Mathf.Max(0.001f, realtime - _lastSampleRealtime);
                Vector2 d = new Vector2(p.x - _lastPlayerPos.x, p.z - _lastPlayerPos.z);
                speed = d.magnitude / dt;
            }
            _lastPlayerPos = p;
            _lastSampleRealtime = realtime;
            _havePlayerPos = true;

            bool inWater = TryFindSurface(p, out float surfaceY);
            if (inWater)
            {
                if (!_haveSurface || Mathf.Abs(surfaceY - _surfaceY) > 1.1f)
                {
                    _surfaceY = surfaceY;
                    _haveSurface = true;
                    ClearState();
                }
                else
                {
                    _surfaceY = surfaceY;
                }

                if (!_wasInWater)
                {
                    QueueImpulse(p, 0.85f);
                    _lastImpulseRealtime = realtime;
                }
                else if (speed > 0.30f && realtime - _lastImpulseRealtime >= Mathf.Lerp(0.24f, 0.10f, Mathf.Clamp01(speed / 6f)))
                {
                    QueueImpulse(p, Mathf.Lerp(0.18f, 0.52f, Mathf.Clamp01(speed / 6f)));
                    _lastImpulseRealtime = realtime;
                }
            }
            else if (_wasInWater)
            {
                QueueImpulse(p, 0.48f);
                _lastImpulseRealtime = realtime;
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

        private void QueueImpulse(Vector3 p, float strength)
        {
            _pendingWorldXZ = new Vector2(p.x, p.z);
            _pendingImpulse = Mathf.Max(_pendingImpulse, strength);
        }

        private void UpdateSimulationCenter()
        {
            Vector3 p = _game.PlayerPosition;
            var desired = new Vector2(
                Mathf.Round(p.x / RecenterStep) * RecenterStep,
                Mathf.Round(p.z / RecenterStep) * RecenterStep);
            if (!_haveCenter || desired != _center)
            {
                _center = desired;
                _haveCenter = true;
                ClearState();
            }
        }

        private void StepSimulation()
        {
            if (_updateMaterial == null || _stateA == null || _stateB == null || !_haveCenter) return;
            RenderTexture source = _aIsCurrent ? _stateA : _stateB;
            RenderTexture target = _aIsCurrent ? _stateB : _stateA;

            float inject = _pendingImpulse;
            Vector2 injectUv = new Vector2(
                (_pendingWorldXZ.x - _center.x) / WorldSize + 0.5f,
                (_pendingWorldXZ.y - _center.y) / WorldSize + 0.5f);
            if (injectUv.x < 0f || injectUv.x > 1f || injectUv.y < 0f || injectUv.y > 1f) inject = 0f;

            _updateMaterial.SetVector(InjectUvId, injectUv);
            _updateMaterial.SetFloat(InjectStrengthId, inject);
            _updateMaterial.SetFloat(InjectRadiusId, 0.014f);
            Graphics.Blit(source, target, _updateMaterial, 0);
            _aIsCurrent = !_aIsCurrent;
            _pendingImpulse = 0f;
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
            if (!_active || _game == null || _camera == null || _displayMaterial == null || !_haveSurface || !_haveCenter) return;
            RenderTexture current = _aIsCurrent ? _stateA : _stateB;
            if (current == null) return;

            _displayMaterial.SetTexture(WaveTexId, current);
            _displayMaterial.SetVector(WaveCenterSizeId, new Vector4(_center.x, _center.y, WorldSize, 1f / WorldSize));
            _displayMaterial.SetFloat(WaveSurfaceYId, _surfaceY);

            Vector3 cameraPos = _camera.transform.position;
            float rangeSq = (WorldSize * 0.75f) * (WorldSize * 0.75f);
            foreach (var chunk in _chunks)
            {
                if (chunk?.Renderer == null || chunk.Filter == null || !chunk.Renderer.enabled
                    || !chunk.Renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = chunk.Filter.sharedMesh;
                if (mesh == null || mesh.subMeshCount < 2 || chunk.Renderer.bounds.SqrDistance(cameraPos) > rangeSq) continue;
                Graphics.DrawMesh(mesh, chunk.Renderer.localToWorldMatrix, _displayMaterial,
                    chunk.Renderer.gameObject.layer, _camera, 1);
            }
        }

        private void ClearState()
        {
            Clear(_stateA);
            Clear(_stateB);
            _aIsCurrent = true;
            _pendingImpulse = 0f;
            _accumulator = 0f;
        }

        private static void Clear(RenderTexture rt)
        {
            if (rt == null || !rt.IsCreated()) return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        private void DestroyResources()
        {
            if (_updateMaterial != null) Destroy(_updateMaterial);
            if (_displayMaterial != null) Destroy(_displayMaterial);
            if (_stateA != null) { _stateA.Release(); Destroy(_stateA); }
            if (_stateB != null) { _stateB.Release(); Destroy(_stateB); }
            _updateMaterial = null;
            _displayMaterial = null;
            _stateA = null;
            _stateB = null;
        }

        private void OnDestroy()
        {
            DestroyResources();
            _chunks.Clear();
            _scan.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
