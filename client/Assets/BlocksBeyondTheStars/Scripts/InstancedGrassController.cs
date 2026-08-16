// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// High-preset near-field grass blades. Samples only grass TOP triangles from the already-built terrain chunk
    /// mesh, then renders tiny cross-blades with GPU instancing. Placement is deterministic and presentation-only:
    /// no blocks, collision, server state or world generation are touched.
    ///
    /// The area-sampling / root-pinned wind / shared dirt mask ideas are an original Unity adaptation inspired by
    /// Christian Ortiz' MIT-licensed stylized-components GrassField study. See docs/developer/STYLIZED_COMPONENTS_ADAPTATION.md.
    /// </summary>
    public sealed class InstancedGrassController : MonoBehaviour
    {
        private static readonly int GrassUvId = Shader.PropertyToID("_GrassUvRect");
        private static readonly int TimeId = Shader.PropertyToID("_GrassTime");
        private static readonly int WindId = Shader.PropertyToID("_WindStrength");
        private static readonly int SeedId = Shader.PropertyToID("_BiomeSeed");
        private static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
        private static readonly int TrampleRadiusId = Shader.PropertyToID("_TrampleRadius");
        private static InstancedGrassController _instance;

        private sealed class DrawBatch
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public int VertexCount;
            public uint IndexCount;
            public readonly List<Matrix4x4[]> Groups = new List<Matrix4x4[]>();
        }

        private readonly Dictionary<int, DrawBatch> _batches = new Dictionary<int, DrawBatch>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>(512);
        private readonly List<Vector3> _vertices = new List<Vector3>(4096);
        private readonly List<Vector3> _normals = new List<Vector3>(4096);
        private readonly List<Vector2> _uvs = new List<Vector2>(4096);
        private readonly List<Matrix4x4> _matrixScratch = new List<Matrix4x4>(1024);

        private GameBootstrap _game;
        private Camera _camera;
        private Material _material;
        private Mesh _bladeMesh;
        private Rect _grassUv;
        private Texture _atlas;
        private int _worldEpoch = -1;
        private float _nextSetup;
        private float _nextScan;
        private bool _active;
        private const float Range = 20f;
        private const float DensityPerSquareMetre = 2.4f;
        private const int MaxBladesPerChunk = 780;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var host = new GameObject("InstancedGrassController");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<InstancedGrassController>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextSetup) return;
            _nextSetup = Time.unscaledTime + 0.25f;

            if (_game == null) _game = FindFirstObjectByType<GameBootstrap>();
            if (_camera == null) _camera = Camera.main;

            var settings = _game?.Settings;
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            bool wanted = _game != null && _camera != null && settings != null && urp
                          && settings.Preset == QualityPreset.High && !settings.ReducedEffects
                          && !_game.SpaceViewActive && _game.Atlas != null && _game.Content != null;
            if (!wanted) { _active = false; return; }

            if (_material == null || _bladeMesh == null || _atlas != _game.Atlas.Texture)
            {
                if (!BuildResources()) { _active = false; return; }
            }

            if (_worldEpoch != _game.WorldEpoch)
            {
                _worldEpoch = _game.WorldEpoch;
                _batches.Clear();
                _nextScan = 0f;
            }

            _active = true;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1.5f;
                RefreshNearChunks();
            }
        }

        private bool BuildResources()
        {
            DestroyResources();
            Shader shader = Resources.Load<Shader>("InstancedGrass") ?? Shader.Find("BlocksBeyondTheStars/InstancedGrass");
            var grass = _game?.Content?.GetBlock("grass");
            if (shader == null || grass == null || grass.NumericId.Value == 0 || _game?.Atlas == null) return false;

            _grassUv = _game.Atlas.TileUv(grass.NumericId.Value);
            _atlas = _game.Atlas.Texture;
            _bladeMesh = CreateCrossBlade();
            _material = new Material(shader)
            {
                name = "InstancedGrass",
                enableInstancing = true,
                renderQueue = 2470,
            };
            _material.SetVector(GrassUvId, new Vector4(_grassUv.xMin, _grassUv.yMin, _grassUv.xMax, _grassUv.yMax));
            return true;
        }

        private void RefreshNearChunks()
        {
            _renderers.Clear();
            _game.GetComponentsInChildren<MeshRenderer>(false, _renderers);
            Vector3 cameraPos = _camera.transform.position;
            float rangeSq = Range * Range;
            var alive = new HashSet<int>();

            foreach (var renderer in _renderers)
            {
                if (renderer == null || renderer.transform.parent != _game.transform
                    || !renderer.gameObject.name.StartsWith("Chunk ", StringComparison.Ordinal)
                    || renderer.bounds.SqrDistance(cameraPos) > rangeSq) continue;

                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || mesh.subMeshCount < 1) continue;

                int key = renderer.GetInstanceID();
                alive.Add(key);
                uint indexCount = mesh.GetIndexCount(0);
                if (!_batches.TryGetValue(key, out var batch) || batch.Mesh != mesh
                    || batch.VertexCount != mesh.vertexCount || batch.IndexCount != indexCount)
                {
                    _batches[key] = BuildBatch(renderer, mesh, indexCount);
                }
            }

            var remove = new List<int>();
            foreach (var kv in _batches)
                if (!alive.Contains(kv.Key) || kv.Value.Renderer == null) remove.Add(kv.Key);
            foreach (int key in remove) _batches.Remove(key);
        }

        private DrawBatch BuildBatch(MeshRenderer renderer, Mesh mesh, uint indexCount)
        {
            var batch = new DrawBatch
            {
                Renderer = renderer,
                Mesh = mesh,
                VertexCount = mesh.vertexCount,
                IndexCount = indexCount,
            };

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _matrixScratch.Clear();
            mesh.GetVertices(_vertices);
            mesh.GetNormals(_normals);
            mesh.GetUVs(0, _uvs);
            int[] indices = mesh.GetIndices(0);
            Matrix4x4 l2w = renderer.localToWorldMatrix;

            for (int tri = 0; tri + 2 < indices.Length && _matrixScratch.Count < MaxBladesPerChunk; tri += 3)
            {
                int ia = indices[tri];
                int ib = indices[tri + 1];
                int ic = indices[tri + 2];
                if (ia >= _vertices.Count || ib >= _vertices.Count || ic >= _vertices.Count
                    || ia >= _uvs.Count || ib >= _uvs.Count || ic >= _uvs.Count) continue;

                // Use the authored/mesher vertex normal rather than triangle winding: either clockwise convention
                // can be valid, but only genuinely world-UP grass surfaces should grow blades.
                Vector3 avgNormal = Vector3.up;
                if (ia < _normals.Count && ib < _normals.Count && ic < _normals.Count)
                {
                    avgNormal = l2w.MultiplyVector((_normals[ia] + _normals[ib] + _normals[ic]) / 3f).normalized;
                }
                if (avgNormal.y < 0.72f) continue;

                Vector2 uv = (_uvs[ia] + _uvs[ib] + _uvs[ic]) / 3f;
                if (uv.x < _grassUv.xMin || uv.x > _grassUv.xMax || uv.y < _grassUv.yMin || uv.y > _grassUv.yMax) continue;

                Vector3 a = l2w.MultiplyPoint3x4(_vertices[ia]);
                Vector3 b = l2w.MultiplyPoint3x4(_vertices[ib]);
                Vector3 c = l2w.MultiplyPoint3x4(_vertices[ic]);
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area < 0.001f) continue;

                int samples = Mathf.Clamp(Mathf.RoundToInt(area * DensityPerSquareMetre), 1, 3);
                for (int s = 0; s < samples && _matrixScratch.Count < MaxBladesPerChunk; s++)
                {
                    float r1 = Hash01(tri, s, renderer.GetInstanceID());
                    float r2 = Hash01(tri + 71, s + 17, _worldEpoch + 313);
                    float sr1 = Mathf.Sqrt(r1);
                    float u = 1f - sr1;
                    float v = r2 * sr1;
                    float w = 1f - u - v;
                    Vector3 p = a * u + b * v + c * w + Vector3.up * 0.012f;
                    float yaw = Hash01(tri + 193, s + 29, renderer.GetInstanceID()) * 360f;
                    float height = Mathf.Lerp(0.34f, 0.68f, Hash01(tri + 389, s + 43, _worldEpoch + 911));
                    float width = Mathf.Lerp(0.70f, 1.15f, Hash01(tri + 557, s + 59, renderer.GetInstanceID()));
                    _matrixScratch.Add(Matrix4x4.TRS(p, Quaternion.Euler(0f, yaw, 0f), new Vector3(width, height, width)));
                }
            }

            for (int offset = 0; offset < _matrixScratch.Count; offset += 1023)
            {
                int count = Mathf.Min(1023, _matrixScratch.Count - offset);
                var group = new Matrix4x4[count];
                _matrixScratch.CopyTo(offset, group, 0, count);
                batch.Groups.Add(group);
            }
            return batch;
        }

        private void LateUpdate()
        {
            if (!_active || _game == null || _camera == null || _material == null || _bladeMesh == null || _game.SpaceViewActive) return;
            Vector3 player = _game.PlayerPosition;
            _material.SetFloat(TimeId, _game.WorldTime);
            _material.SetFloat(WindId, Mathf.Clamp01(_game.WindSpeed));
            _material.SetFloat(SeedId, StableSeed01(_game.WorldSeed, _game.LocationName));
            _material.SetVector(PlayerPosId, new Vector4(player.x, player.y, player.z, 1f));
            _material.SetFloat(TrampleRadiusId, 1.45f);

            foreach (var batch in _batches.Values)
            {
                if (batch.Renderer == null || !batch.Renderer.enabled || !batch.Renderer.gameObject.activeInHierarchy) continue;
                int layer = batch.Renderer.gameObject.layer;
                foreach (var group in batch.Groups)
                {
                    if (group.Length > 0)
                        Graphics.DrawMeshInstanced(_bladeMesh, 0, _material, group, group.Length, null,
                            UnityEngine.Rendering.ShadowCastingMode.Off, true, layer, _camera);
                }
            }
        }

        private static Mesh CreateCrossBlade()
        {
            const float half = 0.055f;
            var mesh = new Mesh { name = "VoidcraftGrassBlade" };
            mesh.vertices = new[]
            {
                new Vector3(-half,0,0), new Vector3(half,0,0), new Vector3(-half*0.25f,1,0), new Vector3(half*0.25f,1,0),
                new Vector3(0,0,-half), new Vector3(0,0,half), new Vector3(0,1,-half*0.25f), new Vector3(0,1,half*0.25f),
            };
            mesh.uv = new[]
            {
                new Vector2(0,0), new Vector2(1,0), new Vector2(0.35f,1), new Vector2(0.65f,1),
                new Vector2(0,0), new Vector2(1,0), new Vector2(0.35f,1), new Vector2(0.65f,1),
            };
            mesh.triangles = new[] { 0,2,1, 1,2,3, 4,6,5, 5,6,7 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 69069);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float StableSeed01(long worldSeed, string location)
        {
            unchecked
            {
                ulong h = (ulong)worldSeed ^ 1469598103934665603UL;
                foreach (char c in location ?? string.Empty) { h ^= c; h *= 1099511628211UL; }
                return (h & 0xFFFFFFUL) / 16777215f;
            }
        }

        private void DestroyResources()
        {
            _batches.Clear();
            if (_material != null) Destroy(_material);
            if (_bladeMesh != null) Destroy(_bladeMesh);
            _material = null;
            _bladeMesh = null;
            _atlas = null;
        }

        private void OnDestroy()
        {
            DestroyResources();
            if (_instance == this) _instance = null;
        }
    }
}
