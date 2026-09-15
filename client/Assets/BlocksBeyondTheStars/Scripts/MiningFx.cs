// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Mining/placing feedback (M27 polish): a wireframe selection box on the block the player is
    /// looking at, and a small debris burst when a block is mined or placed. Code-built (one mesh for
    /// twelve thin edges + particles) on the always-included Unlit/Color shader — no assets, no new
    /// shader. Render-only; the server stays authoritative over the actual block changes.
    /// </summary>
    public sealed class MiningFx : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public float Reach = 6f;

        private GameObject _outline;
        private Mesh _outlineMesh;
        private static readonly Color SelectionColor = new(0.28f, 0.52f, 0.58f);
        private Material _outlineMat;
        private Material _flashMat;
        private bool _subscribed;

        private void Start()
        {
            _outlineMat = Mat(SelectionColor);
            _flashMat = Mat(new Color(1f, 0.86f, 0.5f));
            _outline = BuildWireCube(_outlineMat, out _outlineMesh);
            _outline.transform.SetParent(transform, false);
            _outline.SetActive(false);
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.BlockChangeApplied += OnBlockApplied;
                Game.Network.MiningProgressReceived += OnMineProgress;
                _subscribed = true;
            }

            UpdateOutline();
        }

        private void UpdateOutline()
        {
            if (_outline == null || Camera == null || Game == null || Game.MenuOpen || Game.SpaceViewActive)
            {
                if (_outline != null)
                {
                    _outline.SetActive(false);
                }

                return;
            }

            if (AimVoxel(out int bx, out int by, out int bz))
            {
                _outline.transform.position = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
                _outline.SetActive(true);

                // A thin muted outline leaves glass useful; orange still communicates mining progress.
                float frac = (bx == Mathf.FloorToInt(Game.SceneX(_crackX)) && by == _crackY
                    && bz == Mathf.FloorToInt(Game.SceneZ(_crackZ)) && Time.time - _crackAt < 0.4f) ? _crackFrac : 0f;
                _outlineMat.color = ShaderColor.Srgb(Color.Lerp(SelectionColor, new Color(1f, 0.45f, 0.12f), frac));
            }
            else
            {
                _outline.SetActive(false);
            }
        }

        /// <summary>Marches the voxel grid (Amanatides &amp; Woo) along the aim ray and returns the first
        /// targetable cell within <see cref="Reach"/> — mirroring <c>PlayerController.AimTarget</c> so the
        /// selection box highlights EXACTLY the block the mine/place click would hit (voxel world OR a parked
        /// ship cell). Reading the world directly instead of <see cref="Physics.Raycast"/> is what kills the
        /// "black outline flickers in the air" glitch: the old ray could snap the box onto a creature/ship/
        /// speeder collider hovering in mid-air, linger on a just-mined cell whose collider hadn't re-baked
        /// yet, or blink off for a frame while a chunk collider was mid-rebuild (the same B32 desync the drill
        /// already side-steps). The march sees none of that — it only knows the authoritative block data.</summary>
        private bool AimVoxel(out int bx, out int by, out int bz)
        {
            bx = by = bz = 0;
            if (Game?.World == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);

            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 80 && t <= Reach; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if ((!id.IsAir && !IsFluid(id)) || !Game.LandedShipBlockAt(x, y, z, out _, out _).IsAir)
                {
                    bx = x; by = y; bz = z;
                    return true;
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Water/lava are passed through when aiming (no collider — you swim/sink into them), matching
        /// the mine/place march so the box never sits on a fluid you cannot target.</summary>
        private bool IsFluid(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
        {
            var key = Game?.Content?.BlockById(id)?.Key;
            return key is "water" or "lava";
        }

        private int _crackX, _crackY, _crackZ;
        private float _crackFrac, _crackAt;
        private BlocksBeyondTheStars.Shared.Primitives.BlockId _crackBlock; // what is being mined (sampled pre-break)

        private void OnMineProgress(MiningProgress m)
        {
            _crackX = m.X;
            _crackY = m.Y;
            _crackZ = m.Z;
            _crackFrac = Mathf.Clamp01(m.Fraction);
            _crackAt = Time.time;
            if (Game?.World != null)
            {
                // Retain the local target for its progress and pickup animation. Debris uses the
                // confirmed old/new material supplied by BlockChangeApplied, including remote edits.
                _crackBlock = Game.World.GetBlock(m.X, m.Y, m.Z);
                if (_crackBlock.IsAir)
                    _crackBlock = Game.LandedShipBlockAt(m.X, m.Y, m.Z, out _, out _);
            }
        }

        private void OnBlockApplied(Vector3i cell, BlockId oldId, BlockId newId)
        {
            if (Game == null) return;
            if (oldId == newId || IsFluid(oldId) || IsFluid(newId)) return;
            var pos = Game.ScenePos(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            // Remote construction still updates the world, but does not allocate invisible impact effects.
            if (Camera == null || (Camera.transform.position - pos).sqrMagnitude > 24f * 24f) return;
            bool tracked = cell.X == _crackX && cell.Y == _crackY && cell.Z == _crackZ;
            var id = !newId.IsAir ? newId : oldId;
            var block = Game?.Content?.BlockById(id);
            Color debris = new(0.45f, 0.42f, 0.36f);
            if (block != null && BlockSurfaceLibrary.Contains(block.Key))
                debris = BlockSurfaceLibrary.Sample(block.Key, 0.43f, 0.57f).Albedo;
            else if (block?.Color is int packed)
                debris = new Color(((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f, (packed & 255) / 255f);
            bool reduced = Game?.Settings?.ReducedEffects == true;
            SpawnBurst(pos, debris, reduced);
            if (!newId.IsAir)
            {
                return;
            }

            if (!reduced && tracked) FlashAt(pos);
            if (tracked && _crackBlock.Value != 0)
            {
                HudUi.Instance?.FlyPickup(pos, _crackBlock); // mined tile flies into the hotbar
                _crackBlock = default;
            }
        }

        /// <summary>A small contact glint on the mined cell, omitted by reduced-effects settings.</summary>
        private void FlashAt(Vector3 pos)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(p);
            p.transform.SetParent(transform, false);
            p.transform.position = pos;
            p.transform.localScale = Vector3.one * 0.14f;
            p.GetComponent<Renderer>().sharedMaterial = _flashMat;
            p.AddComponent<FlashFx>();
        }

        private static Material _burstMat;

        /// <summary>A small debris/dust puff when a block is mined or placed: a one-shot ParticleSystem burst of
        /// soft alpha bits in the dig/place colour that arc out under gravity and fade, then self-destroys. Replaces
        /// the old Unlit debris cubes.</summary>
        private void SpawnBurst(Vector3 pos, Color color, bool reduced)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/ParticleAlpha");
            if (shader == null)
            {
                return;
            }

            _burstMat ??= new Material(shader) { mainTexture = SoftDot() };

            var go = new GameObject("MineBurst");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(reduced ? 0.12f : 0.3f, reduced ? 0.2f : 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(reduced ? 0.6f : 1.2f, reduced ? 1.3f : 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(reduced ? 0.04f : 0.06f, reduced ? 0.07f : 0.14f);
            main.startColor = ShaderColor.Srgb(color);
            main.maxParticles = reduced ? 3 : 12;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.8f; // chips arc and fall
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(reduced ? 3 : 10)) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere; // bias the spray upward/outward from the face
            shape.radius = 0.25f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _burstMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
        }

        private static Texture2D _softDot;

        /// <summary>A soft round dot (opaque core → transparent rim) for the debris puff bits.</summary>
        private static Texture2D SoftDot()
        {
            if (_softDot != null)
            {
                return _softDot;
            }

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, d));
            }

            tex.SetPixels(px);
            tex.Apply();
            _softDot = tex;
            return tex;
        }

        private static GameObject BuildWireCube(Material mat, out Mesh mesh)
        {
            const float t = 0.015f;   // edge thickness
            const float s = 1.04f;    // edge length (slightly proud of the block)
            var root = new GameObject("BlockOutline");
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.SetActive(false);
            var cube = source.GetComponent<MeshFilter>().sharedMesh;
            var edges = new CombineInstance[12];
            int index = 0;

            // Twelve edges of a unit cube centred on the root: 4 along each axis.
            for (int a = 0; a < 3; a++)
            {
                for (int i = 0; i < 4; i++)
                {
                    float u = (i & 1) == 0 ? -0.5f : 0.5f;
                    float v = (i & 2) == 0 ? -0.5f : 0.5f;
                    Vector3 pos, scale;
                    if (a == 0) { pos = new Vector3(0f, u, v); scale = new Vector3(s, t, t); }
                    else if (a == 1) { pos = new Vector3(u, 0f, v); scale = new Vector3(t, s, t); }
                    else { pos = new Vector3(u, v, 0f); scale = new Vector3(t, t, s); }

                    edges[index++] = new CombineInstance { mesh = cube,
                        transform = Matrix4x4.TRS(pos, Quaternion.identity, scale) };
                }
            }

            mesh = new Mesh { name = "Block selection edges" };
            mesh.CombineMeshes(edges, true, true);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Destroy(source);
            return root;
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.BlockChangeApplied -= OnBlockApplied;
                Game.Network.MiningProgressReceived -= OnMineProgress;
            }
            if (_outline != null) Destroy(_outline);
            if (_outlineMesh != null) Destroy(_outlineMesh);
            if (_outlineMat != null) Destroy(_outlineMat);
            if (_flashMat != null) Destroy(_flashMat);
        }

        private static Material Mat(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(c) };
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        /// <summary>The contact glint grows a few centimeters and vanishes in 0.08 s.</summary>
        private sealed class FlashFx : MonoBehaviour
        {
            private const float Life = 0.08f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                transform.localScale = Vector3.one * Mathf.Lerp(0.14f, 0.22f, _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>A short-lived debris cube: arcs out under gravity, shrinks, then self-destroys.</summary>
        private sealed class FxParticle : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.6f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                Vel += Vector3.down * 9f * Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.12f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
