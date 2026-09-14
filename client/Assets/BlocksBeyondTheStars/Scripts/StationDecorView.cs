// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Compact ceramic/graphite fixtures for server-provided ship stations. Every solid detail is
    /// combined into one cached draw, with a second draw for narrow display/lamp inlays. All fixtures
    /// stay over their solid marker cell, clear of neighboring walkways; guarded casing may cover the
    /// existing cube's faces with a one-millimeter offset. They never add collision or
    /// authoritative state. Positions follow <see cref="GameBootstrap.ScenePos"/> across the torus wrap.
    /// </summary>
    public sealed class StationDecorView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly Color Cyan = new Color(0.36f, 0.79f, 0.91f);
        private static readonly Color Amber = new Color(1f, 0.66f, 0.32f);
        private NetShipStation[] _builtFor;
        private sealed class Fixture
        {
            internal GameObject Go;
            internal Vector3 World;
            internal string Type;
            internal Renderer Surface;
            internal Bounds Bounds;
            internal NetShipStation Marker;
            internal bool Cased;
        }

        private readonly List<Fixture> _decor = new();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            RefreshFixtures();
        }

        private void RefreshFixtures()
        {
            if (!ReferenceEquals(_builtFor, Game.Stations))
            {
                Rebuild();
            }
            else
            {
                // Station metadata can arrive before its ship/world cells. Do not cover an unverified
                // marker; rebuild the shared variant only when the actual cube becomes known or changes.
                foreach (var fixture in _decor)
                {
                    if (fixture.Cased != HasLegacyMarker(fixture.Marker))
                    {
                        Rebuild();
                        break;
                    }
                }
            }

            foreach (var fixture in _decor)
            {
                if (fixture.Go != null)
                {
                    var world = fixture.World;
                    fixture.Go.transform.position = Game.ScenePos(world.x, world.y, world.z);
                    // Cache only the opaque equipment renderer, never the hologram or luminous inlays.
                    fixture.Bounds = fixture.Surface.bounds;
                }
            }
        }

        /// <summary>Pick the visible fixture before a ray passes through its collider-free display.</summary>
        internal bool TryPick(GameBootstrap owner, Ray ray, float range, float obstructionDistance, out string type, out bool occluded)
        {
            type = string.Empty;
            occluded = false;
            if (owner == null || Game != owner || !isActiveAndEnabled || range <= 0f)
            {
                return false;
            }

            // Network station replacement and a wrap can happen before this component's Update.
            RefreshFixtures();
            float nearest = range;
            Fixture picked = null;
            foreach (var fixture in _decor)
            {
                if (fixture.Surface == null || !fixture.Surface.enabled || !fixture.Go.activeInHierarchy
                    || !fixture.Bounds.IntersectRay(ray, out float distance) || distance > nearest)
                {
                    continue;
                }

                nearest = distance;
                picked = fixture;
            }

            if (picked == null)
            {
                return false;
            }

            occluded = nearest > obstructionDistance + 0.001f;
            if (occluded)
            {
                return false;
            }

            type = picked.Type;
            return true;
        }

        private void Rebuild()
        {
            foreach (var fixture in _decor)
            {
                if (fixture.Go != null)
                {
                    fixture.Go.SetActive(false);
                    EquipmentGeometry.DestroyResource(fixture.Go);
                }
            }

            _decor.Clear();
            _builtFor = Game.Stations;
            foreach (var station in _builtFor ?? System.Array.Empty<NetShipStation>())
            {
                bool cased = HasLegacyMarker(station);
                var root = BuildFixture(transform, station.Type, cased);
                if (root == null)
                {
                    continue;
                }

                root.transform.localRotation = Quaternion.Euler(0f, station.Yaw, 0f);

                // X/Z are the marker cell's center; Y is its bottom. Do not shift into an adjacent cell.
                _decor.Add(new Fixture
                {
                    Go = root,
                    World = new Vector3(station.X, station.Y + 1f, station.Z),
                    Type = station.Type,
                    Surface = root.transform.Find("Housing").GetComponent<Renderer>(),
                    Marker = station,
                    Cased = cased,
                });
                if (station.Type == "cockpit")
                {
                    var holo = new GameObject("LocalSystemProjection");
                    holo.transform.SetParent(root.transform, false);
                    holo.transform.localPosition = new Vector3(0f, 0.76f, -0.05f);
                    var view = holo.AddComponent<HoloMap>();
                    view.Game = Game;
                    view.ViewCamera = Camera;
                }
            }
        }

        internal static GameObject BuildModel(Transform parent, string type)
            => BuildFixture(parent, type, false);

        private bool HasLegacyMarker(NetShipStation station)
        {
            if (Game?.Content == null || station == null) return false;
            int x = Mathf.FloorToInt(station.X), y = Mathf.FloorToInt(station.Y), z = Mathf.FloorToInt(station.Z);
            var id = Game.LandedShipBlockAt(x, y, z, out var ship, out var local);
            int shape = 0;
            if (!id.IsAir)
            {
                ship.Shapes.TryGetValue(local, out shape);
            }
            else
            {
                if (Game.World == null) return false;
                id = Game.World.GetBlock(x, y, z);
                shape = Game.World.GetShape(x, y, z);
            }
            return UsesMarkerCasing(station.Type, Game.Content.BlockById(id)?.Key, shape);
        }

        internal static bool UsesMarkerCasing(string type, string key, int shape)
            => ShapeCode.ShapeOf(shape) == 0 && (type, key) is
                ("medbay", "ice") or ("quarters", "carbon") or ("workshop", "stone");

        internal static GameObject BuildFixture(Transform parent, string type, bool cased)
        {
            if (type != "cockpit" && type != "medbay" && type != "lab" && type != "console"
                && type != "workshop" && type != "quarters" && type != "cargo" && type != "life_support")
            {
                return null;
            }

            return EquipmentGeometry.Create(parent, type + " fixture", "station:" + type + (cased ? ":cased" : ""), Cyan, b =>
            {
                if (cased)
                {
                    // Legacy interaction markers use ice/carbon/stone. Cover only a verified full cube,
                    // keeping its saved identity and collision. The small face offset prevents z-fighting;
                    // do not bevel these edges and reveal the unrelated terrain material underneath.
                    b.Box(new Vector3(0f, -0.5f, 0f), new Vector3(1.002f, 1.002f, 1.002f),
                        type == "quarters" ? EquipmentGeometry.Finish.Graphite : EquipmentGeometry.Finish.Ceramic);
                }
                switch (type)
                {
                    case "cockpit": BuildCockpit(b); break;
                    case "workshop": BuildWorkshop(b); break;
                    case "quarters": BuildQuarters(b); break;
                    case "cargo": BuildCargo(b); break;
                    case "medbay": BuildLifeSupport(b, true); break;
                    case "life_support": BuildLifeSupport(b, false); break;
                    default: BuildTerminal(b, type == "lab"); break;
                }
            });
        }

        private static void Plinth(EquipmentGeometry.Builder b, float width, float depth, float height)
        {
            b.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth), EquipmentGeometry.Finish.Graphite, 0.025f);
            b.Box(new Vector3(0f, 0.025f, 0f), new Vector3(width + 0.015f, 0.05f, depth + 0.015f), EquipmentGeometry.Finish.Rubber, 0.012f);
        }

        private static void Display(EquipmentGeometry.Builder b, Vector3 position, Vector2 size, Color color, float tilt = 0f)
        {
            var q = Quaternion.Euler(tilt, 0f, 0f);
            b.Box(position, new Vector3(size.x, size.y, 0.05f), EquipmentGeometry.Finish.Graphite, 0.018f, q);
            b.Box(position + q * new Vector3(0f, 0f, -0.029f), new Vector3(size.x - 0.035f, size.y - 0.035f, 0.012f),
                EquipmentGeometry.Finish.Glass, 0.008f, q);
            for (int i = 0; i < 4; i++)
            {
                float width = (size.x - 0.09f) * (0.50f + i * 0.13f);
                var local = new Vector3(-size.x * 0.5f + 0.045f + width * 0.5f, size.y * 0.30f - i * size.y * 0.18f, -0.037f);
                b.Signal(position + q * local, new Vector2(width, 0.005f), color * (0.74f + i * 0.06f), q);
            }

            b.Signal(position + q * new Vector3(size.x * 0.5f - 0.049f, 0f, -0.037f), new Vector2(0.009f, size.y * 0.54f), color, q);
        }

        private static void BuildCockpit(EquipmentGeometry.Builder b)
        {
            Plinth(b, 0.81f, 0.62f, 0.22f);
            b.Box(new Vector3(0f, 0.255f, -0.02f), new Vector3(0.86f, 0.10f, 0.62f), EquipmentGeometry.Finish.Ceramic, 0.025f);
            b.Box(new Vector3(0f, 0.40f, 0.17f), new Vector3(0.12f, 0.28f, 0.10f), EquipmentGeometry.Finish.Steel, 0.018f);
            Display(b, new Vector3(0f, 0.53f, 0.12f), new Vector2(0.57f, 0.31f), Cyan, 17f);
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(new Vector3(side * 0.355f, 0.315f, -0.06f), new Vector3(0.10f, 0.052f, 0.32f), EquipmentGeometry.Finish.Rubber, 0.012f);
                b.Barrel(new Vector3(side * 0.355f, 0.39f, -0.13f), 0.025f, 0.019f, 0.13f,
                    EquipmentGeometry.Finish.Graphite, 8, Quaternion.Euler(90f, 0f, 0f));
                b.Box(new Vector3(side * 0.30f, 0.17f, -0.318f), new Vector3(0.10f, 0.022f, 0.018f), EquipmentGeometry.Finish.Orange, 0.005f);
            }

            for (int i = 0; i < 6; i++)
            {
                b.Box(new Vector3(-0.16f + i * 0.064f, 0.319f, -0.19f), new Vector3(0.042f, 0.016f, 0.046f),
                    i == 0 ? EquipmentGeometry.Finish.Orange : EquipmentGeometry.Finish.Graphite, 0.004f);
            }

            b.Signal(new Vector3(0f, 0.12f, -0.313f), new Vector2(0.20f, 0.008f), Amber);
        }

        private static void BuildWorkshop(EquipmentGeometry.Builder b)
        {
            Plinth(b, 0.84f, 0.71f, 0.23f);
            for (int i = 0; i < 2; i++)
            {
                b.Box(new Vector3(-0.19f, 0.075f + i * 0.095f, -0.361f), new Vector3(0.38f, 0.078f, 0.016f), EquipmentGeometry.Finish.Ceramic, 0.007f);
                b.Box(new Vector3(-0.19f, 0.075f + i * 0.095f, -0.377f), new Vector3(0.16f, 0.012f, 0.018f), EquipmentGeometry.Finish.Steel, 0.004f);
            }

            b.Box(new Vector3(0f, 0.267f, 0f), new Vector3(0.91f, 0.065f, 0.77f), EquipmentGeometry.Finish.Steel, 0.016f);
            b.Box(new Vector3(0f, 0.304f, -0.02f), new Vector3(0.84f, 0.014f, 0.64f), EquipmentGeometry.Finish.Rubber, 0.004f);
            b.Box(new Vector3(0f, 0.555f, 0.33f), new Vector3(0.80f, 0.48f, 0.055f), EquipmentGeometry.Finish.Graphite, 0.018f);
            b.Box(new Vector3(0f, 0.802f, 0.25f), new Vector3(0.86f, 0.064f, 0.23f), EquipmentGeometry.Finish.Ceramic, 0.018f);
            b.Signal(new Vector3(0f, 0.795f, 0.132f), new Vector2(0.57f, 0.017f), Amber);
            for (int i = 0; i < 5; i++)
            {
                float x = -0.29f + i * 0.14f;
                b.Box(new Vector3(x, 0.53f, 0.294f), new Vector3(0.027f, 0.24f - i % 2 * 0.04f, 0.018f), EquipmentGeometry.Finish.Steel, 0.006f);
                b.Box(new Vector3(x, 0.445f, 0.279f), new Vector3(0.038f, 0.073f, 0.037f),
                    i % 2 == 0 ? EquipmentGeometry.Finish.Orange : EquipmentGeometry.Finish.Rubber, 0.007f);
                b.Box(new Vector3(x, 0.645f, 0.28f), new Vector3(0.068f, 0.025f, 0.032f), EquipmentGeometry.Finish.Steel, 0.006f);
            }

            SampleTray(b, new Vector3(-0.21f, 0.325f, -0.12f));
            b.Box(new Vector3(0.23f, 0.37f, 0f), new Vector3(0.18f, 0.13f, 0.17f), EquipmentGeometry.Finish.Graphite, 0.012f);
            b.Box(new Vector3(0.23f, 0.443f, -0.01f), new Vector3(0.20f, 0.055f, 0.08f), EquipmentGeometry.Finish.Steel, 0.007f);
            b.Barrel(new Vector3(0.32f, 0.38f, -0.01f), 0.011f, 0.011f, 0.12f, EquipmentGeometry.Finish.Steel, 8, Quaternion.Euler(0f, 90f, 0f));
        }

        private static void SampleTray(EquipmentGeometry.Builder b, Vector3 origin)
        {
            b.Box(origin, new Vector3(0.32f, 0.025f, 0.24f), EquipmentGeometry.Finish.Ceramic, 0.009f);
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(origin + new Vector3(side * 0.153f, 0.027f, 0f), new Vector3(0.014f, 0.054f, 0.24f), EquipmentGeometry.Finish.Ceramic, 0.004f);
            }

            b.Box(origin + new Vector3(-0.071f, 0.063f, 0.017f), new Vector3(0.115f, 0.09f, 0.105f), EquipmentGeometry.Finish.Ore, 0.022f, Quaternion.Euler(8f, 24f, 11f));
            b.Box(origin + new Vector3(0.064f, 0.07f, -0.025f), new Vector3(0.080f, 0.105f, 0.077f), EquipmentGeometry.Finish.Crystal, 0.016f, Quaternion.Euler(5f, -16f, -13f));
        }

        private static void BuildTerminal(EquipmentGeometry.Builder b, bool laboratory)
        {
            Plinth(b, 0.76f, 0.65f, 0.24f);
            b.Box(new Vector3(0f, 0.272f, 0f), new Vector3(0.81f, 0.065f, 0.69f), EquipmentGeometry.Finish.Ceramic, 0.018f);
            b.Box(new Vector3(0f, 0.43f, 0.17f), new Vector3(0.082f, 0.27f, 0.095f), EquipmentGeometry.Finish.Steel, 0.014f);
            Display(b, new Vector3(0f, 0.59f, 0.14f), new Vector2(0.56f, 0.34f), laboratory ? new Color(0.45f, 0.89f, 0.63f) : Cyan, 14f);
            if (laboratory)
            {
                SampleTray(b, new Vector3(-0.17f, 0.329f, -0.10f));
                b.Barrel(new Vector3(0.24f, 0.43f, -0.06f), 0.052f, 0.044f, 0.22f, EquipmentGeometry.Finish.Steel, 8, Quaternion.Euler(90f, 0f, 0f));
                b.Box(new Vector3(0.24f, 0.395f, -0.11f), new Vector3(0.056f, 0.025f, 0.01f), EquipmentGeometry.Finish.Orange, 0.004f);
            }
            else
            {
                b.Box(new Vector3(0f, 0.315f, -0.16f), new Vector3(0.40f, 0.027f, 0.16f), EquipmentGeometry.Finish.Graphite, 0.01f);
                for (int i = 0; i < 5; i++)
                {
                    b.Box(new Vector3(-0.15f + i * 0.074f, 0.334f, -0.15f), new Vector3(0.051f, 0.012f, 0.071f), EquipmentGeometry.Finish.Steel, 0.002f);
                }
            }

            b.Signal(new Vector3(-0.21f, 0.15f, -0.328f), new Vector2(0.12f, 0.007f), Cyan);
        }

        private static void BuildLifeSupport(EquipmentGeometry.Builder b, bool medical)
        {
            Plinth(b, 0.70f, 0.62f, 0.12f);
            b.Box(new Vector3(0f, 0.48f, 0.11f), new Vector3(0.71f, 0.80f, 0.40f), EquipmentGeometry.Finish.Ceramic, 0.038f);
            b.Box(new Vector3(0f, 0.47f, -0.105f), new Vector3(0.46f, 0.56f, 0.04f), EquipmentGeometry.Finish.Graphite, 0.02f);
            b.Box(new Vector3(0f, 0.46f, -0.13f), new Vector3(0.34f, 0.43f, 0.018f), EquipmentGeometry.Finish.Glass, 0.025f);
            for (int side = -1; side <= 1; side += 2)
            {
                b.Barrel(new Vector3(side * 0.265f, 0.46f, -0.13f), 0.049f, 0.049f, 0.45f, EquipmentGeometry.Finish.Steel, 8, Quaternion.Euler(90f, 0f, 0f));
                b.Box(new Vector3(side * 0.265f, 0.45f, -0.13f), new Vector3(0.107f, 0.06f, 0.105f), EquipmentGeometry.Finish.Graphite, 0.009f);
            }

            b.Signal(new Vector3(-0.126f, 0.47f, -0.141f), new Vector2(0.008f, 0.31f), Cyan);
            b.Signal(new Vector3(0.126f, 0.47f, -0.141f), new Vector2(0.008f, 0.31f), Cyan);
            if (medical)
            {
                b.Signal(new Vector3(0f, 0.76f, -0.098f), new Vector2(0.078f, 0.021f), Cyan);
                b.Signal(new Vector3(0f, 0.76f, -0.099f), new Vector2(0.021f, 0.078f), Cyan);
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    b.Box(new Vector3(0f, 0.725f + i * 0.033f, -0.098f), new Vector3(0.25f, 0.011f, 0.018f), EquipmentGeometry.Finish.Graphite, 0.003f);
                }
            }

            b.Box(new Vector3(0f, 0.15f, -0.135f), new Vector3(0.31f, 0.039f, 0.034f), EquipmentGeometry.Finish.Orange, 0.007f);
        }

        private static void BuildQuarters(EquipmentGeometry.Builder b)
        {
            // A stowed berth and rest seat fit the existing one-cell station without inventing a short
            // full-size bed or extending non-colliding decor into the ship's shared walkway.
            Plinth(b, 0.79f, 0.73f, 0.19f);
            b.Box(new Vector3(0f, 0.50f, 0.25f), new Vector3(0.81f, 0.65f, 0.20f), EquipmentGeometry.Finish.Ceramic, 0.035f);
            b.Box(new Vector3(0f, 0.51f, 0.139f), new Vector3(0.62f, 0.48f, 0.045f), EquipmentGeometry.Finish.Linen, 0.021f);
            b.Box(new Vector3(0f, 0.24f, -0.065f), new Vector3(0.70f, 0.11f, 0.49f), EquipmentGeometry.Finish.Linen, 0.028f);
            b.Box(new Vector3(0.115f, 0.305f, -0.08f), new Vector3(0.37f, 0.049f, 0.39f), EquipmentGeometry.Finish.Fabric, 0.015f);
            for (int i = 0; i < 3; i++)
            {
                b.Box(new Vector3(0.115f, 0.334f, -0.19f + i * 0.092f), new Vector3(0.33f, 0.008f, 0.035f), EquipmentGeometry.Finish.Orange, 0.002f);
            }

            b.Box(new Vector3(-0.225f, 0.315f, 0.04f), new Vector3(0.20f, 0.09f, 0.22f), EquipmentGeometry.Finish.Linen, 0.022f, Quaternion.Euler(0f, -8f, 0f));
            b.Box(new Vector3(-0.30f, 0.55f, 0.102f), new Vector3(0.045f, 0.30f, 0.026f), EquipmentGeometry.Finish.Rubber, 0.005f);
            b.Box(new Vector3(0f, 0.828f, 0.19f), new Vector3(0.52f, 0.033f, 0.15f), EquipmentGeometry.Finish.Graphite, 0.009f);
            b.Signal(new Vector3(0f, 0.818f, 0.112f), new Vector2(0.36f, 0.012f), Amber);
        }

        private static void BuildCargo(EquipmentGeometry.Builder b)
        {
            Plinth(b, 0.80f, 0.70f, 0.10f);
            b.Box(new Vector3(0f, 0.32f, 0f), new Vector3(0.78f, 0.46f, 0.66f), EquipmentGeometry.Finish.Ceramic, 0.035f);
            b.Box(new Vector3(0f, 0.558f, 0f), new Vector3(0.80f, 0.058f, 0.68f), EquipmentGeometry.Finish.Graphite, 0.018f);
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(new Vector3(side * 0.27f, 0.325f, -0.339f), new Vector3(0.072f, 0.43f, 0.020f), EquipmentGeometry.Finish.Graphite, 0.006f);
                b.Box(new Vector3(side * 0.27f, 0.44f, -0.36f), new Vector3(0.082f, 0.093f, 0.020f), EquipmentGeometry.Finish.Orange, 0.007f);
            }

            b.Box(new Vector3(0f, 0.34f, -0.35f), new Vector3(0.25f, 0.09f, 0.035f), EquipmentGeometry.Finish.Rubber, 0.012f);
            b.Signal(new Vector3(0.07f, 0.21f, -0.335f), new Vector2(0.09f, 0.006f), Amber);
        }

        /// <summary>Builds a specimen only for the vessel whose server snapshot owns it.</summary>
        internal static GameObject BuildSurveySpecimen(Transform parent)
        {
            var specimen = EquipmentGeometry.Create(parent, "VeylSurveySpecimen", "station:veyl-specimen", Cyan, b =>
            {
                b.Box(Vector3.zero, new Vector3(0.18f, 0.018f, 0.15f), EquipmentGeometry.Finish.Steel, 0.008f);
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(new Vector3(side * 0.061f, 0.035f, 0f), new Vector3(0.016f, 0.065f, 0.11f), EquipmentGeometry.Finish.Graphite, 0.004f);
                }

                var q = Quaternion.Euler(0f, 20f, 9f);
                var center = new Vector3(0f, 0.067f, 0f);
                b.Box(center, new Vector3(0.079f, 0.11f, 0.071f), EquipmentGeometry.Finish.Graphite, 0.014f, q);
                b.Signal(center + q * new Vector3(0f, 0f, -0.0365f), new Vector2(0.0025f, 0.070f), Cyan, q);
                b.Signal(center + q * new Vector3(0.011f, -0.021f, -0.0365f), new Vector2(0.023f, 0.0025f), Cyan, q);
            });
            specimen.transform.localPosition = new Vector3(0.20f, 0.325f, -0.25f);
            return specimen;
        }

        /// <summary>A small projection of real star-map bodies; built only once the server map exists.</summary>
        private sealed class HoloMap : MonoBehaviour
        {
            public GameBootstrap Game;
            public Camera ViewCamera;
            private bool _built;

            private void Update()
            {
                var cam = ViewCamera != null ? ViewCamera : Camera.main;
                bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 25f;
                if (near && !_built && Game?.StarMap?.Systems != null)
                {
                    Build();
                }

                Vector3 target = near ? Vector3.one : Vector3.zero;
                transform.localScale = UiKit.ReducedMotion ? target : Vector3.Lerp(transform.localScale, target, Time.deltaTime * 6f);
                if (near && !UiKit.ReducedMotion)
                {
                    transform.Rotate(0f, 8f * Time.deltaTime, 0f, Space.Self);
                }
            }

            private void Build()
            {
                var bodies = new List<NetBody>();
                var map = Game.StarMap;
                NetStarSystem active = null;
                foreach (var system in map.Systems)
                {
                    foreach (var body in system.Bodies)
                    {
                        if (body.Id == map.ActiveLocationId)
                        {
                            active = system;
                            break;
                        }
                    }

                    if (active != null)
                    {
                        break;
                    }
                }

                active ??= map.Systems.Length > 0 ? map.Systems[0] : null;
                if (active == null)
                {
                    return;
                }

                var key = new StringBuilder("station:holo:");
                foreach (var body in active.Bodies)
                {
                    if (bodies.Count >= 5)
                    {
                        break;
                    }

                    bodies.Add(body);
                    key.Append(body.Id).Append(':').Append(body.PlanetType).Append('|');
                }

                EquipmentGeometry.Create(transform, "System bodies", key.ToString(), Cyan, b =>
                {
                    b.Barrel(Vector3.zero, 0.033f, 0.025f, 0.04f, EquipmentGeometry.Finish.Ore);
                    b.Signal(Vector3.back * 0.022f, Vector2.one * 0.035f, Amber);
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        float radius = 0.11f + i * 0.045f;
                        float angle = i * 2.4f;
                        var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                        Color bodyColor = PlanetTypeColor(bodies[i].PlanetType);
                        b.Barrel(pos, 0.018f, 0.015f, 0.025f, EquipmentGeometry.Finish.Crystal);
                        b.Signal(pos + Vector3.back * 0.014f, Vector2.one * 0.024f, bodyColor);
                        b.Signal(pos + Vector3.forward * 0.014f, Vector2.one * 0.024f, bodyColor, Quaternion.Euler(0f, 180f, 0f));
                    }
                });
                _built = true;
            }

            private static Color PlanetTypeColor(string planetType) => (planetType ?? string.Empty) switch
            {
                "ice" or "frozen" or "tundra" => new Color(0.70f, 0.85f, 1f),
                "lava" or "volcanic" or "ashen" => new Color(1f, 0.50f, 0.20f),
                "desert" or "savanna" => new Color(0.90f, 0.75f, 0.45f),
                "jungle" or "forest" or "swamp" or "fungal" => new Color(0.40f, 0.80f, 0.40f),
                "ocean" => new Color(0.30f, 0.60f, 1f),
                "crystal" or "crystal_living" => new Color(0.80f, 0.70f, 1f),
                _ => new Color(0.60f, 0.70f, 0.75f),
            };
        }
    }
}
