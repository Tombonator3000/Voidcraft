// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One bounded, depth-tested response on surviving Veyl rune faces. All state/rewards stay
    /// on the server. The static mesh animates with one time uniform; no terrain or collider is edited.</summary>
    public sealed class VeylSignalFx : MonoBehaviour
    {
        public GameBootstrap Game { get; private set; }
        public bool ReducedEffects => _settings?.ReducedEffects ?? false;
        private ClientSettings _settings;
        private GameBootstrap _boundGame;

        public void Initialize(GameBootstrap game, ClientSettings settings)
        {
            DetachGame();
            Game = game;
            _settings = settings;
            if (isActiveAndEnabled) AttachGame();
        }

        private void AttachGame()
        {
            if (_boundGame != null || Game == null) return;
            _boundGame = Game;
            _boundGame.NetworkInitialized += BindNetwork;
            _boundGame.WorldTransitionStarted += ResetResponse;
            BindNetwork(_boundGame.Network); // Also handles a view initialized after network construction.
        }

        private void DetachGame()
        {
            if (_boundGame != null)
            {
                _boundGame.NetworkInitialized -= BindNetwork;
                _boundGame.WorldTransitionStarted -= ResetResponse;
            }
            _boundGame = null;
            BindNetwork(null);
            ResetResponse();
        }

        private const int VerticesPerNode = 20;
        private const float MaximumDistance = 96f;
        private static readonly int AgeId = Shader.PropertyToID("_Age");
        private static readonly int ReducedId = Shader.PropertyToID("_ReducedEffects");
        private readonly HashSet<string> _seen = new();
        private readonly List<VeylSignalNode> _nodes = new();
        private MaterialPropertyBlock _properties;
        private NetworkClient _network;
        private GameObject _effect;
        private Mesh _mesh;
        private Material _material;
        private MeshRenderer _renderer;
        private Color[] _colors;
        private Vector3i _origin;
        private float _age;
        private AudioSource _audio;
        private float _audioVolume;

        private void Update()
        {
            Advance(Game != null ? Game.WorldDeltaTime : Time.deltaTime);
        }

        private void BindNetwork(NetworkClient network)
        {
            if (ReferenceEquals(_network, network)) return;
            if (_network != null)
            {
                _network.VeylSignalReceived -= Receive;
                _network.BlockChanged -= OnBlockChanged;
                _network.WorldResetReceived -= OnWorldReset;
                _network.Disconnected -= ResetResponse;
            }
            ResetResponse();
            _network = network;
            if (_network != null)
            {
                _network.VeylSignalReceived += Receive;
                _network.BlockChanged += OnBlockChanged;
                _network.WorldResetReceived += OnWorldReset;
                _network.Disconnected += ResetResponse;
            }
        }

        private void Receive(VeylSignalResponse response)
        {
            if (Game?.World == null || Game.Content == null || response == null
                || string.IsNullOrEmpty(response.EventId) || response.EventId.Length > 512
                || response.Nodes == null || response.Nodes.Length == 0
                || response.Nodes.Length > VeylSignalResponse.MaximumNodes
                || _seen.Count >= 64 || !_seen.Add(response.EventId)) return;
            ClearEffect();
            var cells = new HashSet<Vector3i>();
            foreach (var node in response.Nodes)
            {
                if (node == null) continue;
                var p = WorldConstants.CanonicalBlock(new Vector3i(node.X, node.Y, node.Z), Game.Circumference);
                if (!cells.Add(p)) continue;
                var canonical = new VeylSignalNode { X = p.X, Y = p.Y, Z = p.Z };
                var position = Game.ScenePos(p.X + 0.5f, p.Y + 0.5f, p.Z);
                if ((position - Game.PlayerPosition).sqrMagnitude <= MaximumDistance * MaximumDistance
                    && HostVisible(canonical)) _nodes.Add(canonical);
            }
            if (_nodes.Count == 0) return;
            var shader = Shader.Find("BlocksBeyondTheStars/VeylSignalPulse");
            if (shader == null) { _nodes.Clear(); return; }
            var first = _nodes[0];
            _origin = new Vector3i(first.X, first.Y, first.Z);
            _mesh = BuildMesh(_nodes, _origin, Game.Circumference);
            _colors = _mesh.colors;
            _material = new Material(shader) { name = "Veyl response pulse" };
            _material.SetColor("_Tint", ShaderColor.Srgb(new Color(0.32f, 0.85f, 0.92f, 1f)));
            _effect = new GameObject("Veyl signal response");
            _effect.transform.SetParent(transform, false);
            _effect.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _effect.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _age = 0f;
            ApplyPoseAndTime();
            var audio = ClientAudio.Instance;
            if (audio != null)
            {
                // Existing positional one-shot respects master/SFX volume; parenting cancels it on reset.
                audio.At(audio.Has("terrain_scan") ? "terrain_scan" : "scan_ping",
                    _effect.transform.position + new Vector3(0.5f, 0.5f, -0.01f), 0.72f,
                    ReducedEffects ? 0.35f : 0.5f, echo: true, owner: _effect.transform);
                _audio = _effect.GetComponentInChildren<AudioSource>();
                _audioVolume = _audio != null ? _audio.volume : 0f;
            }
        }

        private bool HostVisible(VeylSignalNode node)
            => Game?.World != null && Game.Content != null
                && Game.World.TryGetBlock(node.X, node.Y, node.Z, out var block)
                && Game.Content.BlockById(block)?.Key == "rune_stone"
                && ShapeCode.ShapeOf(Game.World.GetShape(node.X, node.Y, node.Z)) == 0
                && Game.World.TryGetBlock(node.X, node.Y, node.Z - 1, out var front) && front.IsAir;

        private void Advance(float delta)
        {
            if (_effect == null) return;
            _age += Mathf.Max(0f, delta);
            if (_age >= VeylSignalResponse.DurationSeconds) { ClearEffect(); return; }
            // Read-only bounded checks also hide hosts whose chunks were unloaded during the pulse.
            // Only a changed visibility mask uploads colors; vertices, topology and terrain stay fixed.
            RefreshHosts();
            if (_effect != null) ApplyPoseAndTime();
            if (_audio != null) _audio.volume = _audioVolume * Mathf.Clamp01((VeylSignalResponse.DurationSeconds - _age) / 0.4f);
        }

        private void ApplyPoseAndTime()
        {
            _effect.transform.position = Game.ScenePos(_origin.X, _origin.Y, _origin.Z);
            _properties ??= new MaterialPropertyBlock();
            _properties.SetFloat(AgeId, _age);
            _properties.SetFloat(ReducedId, ReducedEffects ? 1f : 0f);
            _renderer.SetPropertyBlock(_properties);
        }

        private void OnBlockChanged(BlockChanged _) => RefreshHosts();

        private void RefreshHosts()
        {
            if (_mesh == null) return;
            bool changed = false, any = false;
            for (int i = 0; i < _nodes.Count; i++)
            {
                int first = i * VerticesPerNode;
                // Removal/covering is terminal for this short effect; replacing a host does not resurrect it.
                if (_colors[first].a > 0f && !HostVisible(_nodes[i]))
                {
                    for (int j = 0; j < VerticesPerNode; j++) _colors[first + j].a = 0f;
                    changed = true;
                }
                any |= _colors[first].a > 0f;
            }
            if (!any) { ClearEffect(); return; }
            if (changed) _mesh.colors = _colors;
        }

        private static Mesh BuildMesh(IReadOnlyList<VeylSignalNode> nodes, Vector3i origin, int circumference)
        {
            var vertices = new List<Vector3>(nodes.Count * VerticesPerNode);
            var colors = new List<Color>(nodes.Count * VerticesPerNode);
            var phases = new List<Vector2>(nodes.Count * VerticesPerNode);
            var triangles = new List<int>(nodes.Count * 30);
            void Segment(Vector2 a, Vector2 b, Vector3 center, float delay)
            {
                var direction = (b - a).normalized;
                var side = new Vector2(-direction.y, direction.x) * 0.013f;
                int offset = vertices.Count;
                foreach (var p in new[] { a - side, a + side, b + side, b - side })
                {
                    vertices.Add(center + new Vector3(p.x, p.y, 0f));
                    colors.Add(Color.white);
                    phases.Add(new Vector2(delay, 0f));
                }
                triangles.Add(offset); triangles.Add(offset + 1); triangles.Add(offset + 2);
                triangles.Add(offset); triangles.Add(offset + 2); triangles.Add(offset + 3);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var center = new Vector3(WorldConstants.WrapDeltaX(n.X - origin.X, circumference) + 0.5f,
                    n.Y - origin.Y + 0.5f, WorldConstants.WrapDeltaZ(n.Z - origin.Z, circumference) - 0.008f);
                float delay = nodes.Count > 1 ? i * 1.3f / (nodes.Count - 1) : 0f;
                var top = new Vector2(0f, 0.31f); var right = new Vector2(0.2f, 0f);
                var bottom = new Vector2(0f, -0.31f); var left = new Vector2(-0.2f, 0f);
                Segment(top, right, center, delay); Segment(right, bottom, center, delay);
                Segment(bottom, left, center, delay); Segment(left, top, center, delay);
                Segment(new Vector2(0f, -0.20f), new Vector2(0f, 0.20f), center, delay);
            }
            var mesh = new Mesh { name = "Veyl response rune inlays" };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, phases);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        private void OnWorldReset(WorldReset _) => ResetResponse();
        private void ResetResponse() { ClearEffect(); _seen.Clear(); }
        private void ClearEffect()
        {
            if (_effect != null) _effect.SetActive(false);
            Release(_effect); Release(_mesh); Release(_material);
            _effect = null; _mesh = null; _material = null; _renderer = null; _colors = null;
            _nodes.Clear();
            _audio = null;
            _properties?.Clear();
        }
        private static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        private void OnEnable() => AttachGame();
        private void OnDisable() => DetachGame();
        private void OnDestroy() => DetachGame();
    }
}
