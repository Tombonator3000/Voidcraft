// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders + collides the world's doors (<see cref="DoorList"/>). A doorway opening stays air in the
    /// voxel world; this fills it with a dynamic door whose <see cref="BoxCollider"/> blocks the player while
    /// closed and lifts while open. Two kinds: sci-fi <b>slide</b> doors (two panels swoosh apart) and
    /// <b>hinge</b> doors (a leaf swings ~90°). The server owns open/closed state — slide doors auto-open near
    /// players, hinge doors toggle on E (see <see cref="NearestHinge"/>). Mirrors <see cref="NpcView"/>.
    /// </summary>
    public sealed class DoorView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Set so the player controller can find the hinge door it should toggle on E.</summary>
        public static DoorView Instance { get; private set; }

        private const float Height = 2.8f;      // covers the 3-tall doorway, standing on the floor
        private const float Thickness = 0.18f;
        private const float AnimSpeed = 6f;     // how fast a door visually slides/swings toward its state

        private sealed class Door
        {
            public GameObject Go;
            public Transform Pivot;        // rotates the whole door so the wall axis lines up
            public Transform PanelA, PanelB; // slide: the two panels; hinge: PanelA is the leaf
            public BoxCollider Collider;
            public string Kind;
            public Vector3 World;          // canonical world pos (gap centre, floor)
            public float Width;
            public bool AxisX;
            public bool Open;
            public float Anim;             // 0 closed → 1 open, eased toward Open
            public Transform Field;        // energy door: the translucent blue field shown in the open doorway
            public Material FieldMat;      // its material (alpha fades in with Anim) — item 35
        }

        private readonly Dictionary<int, Door> _doors = new Dictionary<int, Door>();
        private bool _subscribed;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.DoorsReceived += OnDoors;
                _subscribed = true;
            }

            foreach (var d in _doors.Values)
            {
                d.Go.transform.position = Game != null ? Game.ScenePos(d.World.x, d.World.y, d.World.z) : d.World;

                float target = d.Open ? 1f : 0f;
                d.Anim = Mathf.MoveTowards(d.Anim, target, Time.deltaTime * AnimSpeed);
                Animate(d);

                // Use the actual opening, including panel thickness, rather than one animation fraction
                // for both a one-meter door and a three-meter ship hatch.
                if (d.Collider != null)
                {
                    d.Collider.enabled = !HasPassageClearance(d.Kind, d.Width, d.Anim);
                }
            }
        }

        /// <summary>Door kinds that swing on a single leaf and are opened by hand with E. The wooden door is the
        /// cheap early-game variant of the hinge door, so it looks and behaves the same way — only the material
        /// (and the recipe: wood instead of metal panels + a gear) differs.</summary>
        private static bool IsHinged(string kind) => kind == "hinge" || kind == "wood";

        private void Animate(Door d)
        {
            float w = d.Width;
            if (IsHinged(d.Kind))
            {
                // Swing the leaf around its jamb edge by up to ~96°.
                d.PanelA.localRotation = Quaternion.Euler(0f, -d.Anim * 96f, 0f);
            }
            else
            {
                // Retract the two panels sideways into the jambs.
                float slide = (w * 0.5f) * d.Anim * 0.92f;
                d.PanelA.localPosition = new Vector3(-w * 0.25f - slide, Height * 0.5f, 0f);
                d.PanelB.localPosition = new Vector3(w * 0.25f + slide, Height * 0.5f, 0f);
            }

            // Energy field: fade it in as the door opens (invisible when closed) with a faint shimmer, so the
            // open doorway shows a passable blue membrane (item 35).
            if (d.FieldMat != null)
            {
                float shimmer = UiKit.ReducedMotion ? 1f : 0.96f + 0.04f * Mathf.Sin(Time.time * 1.8f);
                float alpha = 0.16f * d.Anim * shimmer;
                d.FieldMat.SetColor(FieldColorId, ShaderColor.Srgb(new Color(0.35f, 0.80f, 1f, alpha)));
            }
        }

        private void OnDoors(DoorList m)
        {
            var seen = new HashSet<int>();
            foreach (var nd in m.Doors)
            {
                seen.Add(nd.Id);
                _doors.TryGetValue(nd.Id, out var d);
                // Server registries reuse IDs after ship arrivals/removals and world changes. A reused
                // number must not keep the previous doorway's transform, shape or collider.
                if (d != null && (d.Kind != nd.Kind || d.AxisX != nd.AxisX || !Mathf.Approximately(d.Width, Mathf.Max(1f, nd.Width))
                    || d.World != new Vector3(nd.X, nd.Y, nd.Z)))
                {
                    DestroyDoor(d);
                    _doors.Remove(nd.Id);
                    d = null;
                }

                if (d == null)
                {
                    d = Build(nd);
                    _doors[nd.Id] = d;
                }

                if (d.Open != nd.Open)
                {
                    d.Open = nd.Open;
                    PlayDoorSfx(d);
                }
            }

            if (_doors.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _doors.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    DestroyDoor(_doors[id]);
                    _doors.Remove(id);
                }
            }
        }

        private Door Build(NetDoor nd)
        {
            var go = new GameObject($"Door {nd.Kind} {nd.Id}");
            go.transform.SetParent(transform, false);
            go.transform.position = Game != null ? Game.ScenePos(nd.X, nd.Y, nd.Z) : new Vector3(nd.X, nd.Y, nd.Z);

            // Build everything in an X-aligned frame (wall runs along local X); rotate 90° for a Z wall.
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localRotation = nd.AxisX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

            float w = Mathf.Max(1f, nd.Width);
            bool hinge = IsHinged(nd.Kind);
            bool wood = nd.Kind == "wood";
            Transform a, b = null;
            if (hinge)
            {
                a = new GameObject("LeafPivot").transform;
                a.SetParent(pivot, false);
                a.localPosition = new Vector3(-w * 0.5f, 0f, 0f);
                var leaf = BuildPanel(a, w * 0.96f, wood, true);
                leaf.transform.localPosition = new Vector3(w * 0.5f, Height * 0.5f, 0f);
            }
            else
            {
                a = BuildPanel(pivot, w * 0.49f, wood, false).transform;
                b = BuildPanel(pivot, w * 0.49f, wood, false).transform;
            }

            BuildFrame(pivot, w, wood);

            // A solid collider that blocks the player while closed (the player uses a CharacterController).
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, Height * 0.5f, 0f);
            col.size = nd.AxisX ? new Vector3(w, Height, Thickness * 2f) : new Vector3(Thickness * 2f, Height, w);

            // Energy door (item 35): a translucent blue energy field filling the opening, shown only while open
            // (the panels still slide apart). The field is purely visual + passable — no collider — so you walk
            // through it; the door's own collider above handles blocking while closed.
            Transform field = null;
            Material fieldMat = null;
            if (nd.Kind == "energy")
            {
                var fieldGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var fieldLease = fieldGo.AddComponent<EquipmentGeometryLease>();
                StripCollider(fieldGo);
                fieldGo.transform.SetParent(pivot, false);
                fieldGo.transform.localPosition = new Vector3(0f, Height * 0.5f, 0f);
                fieldGo.transform.localScale = new Vector3(w * 0.98f, Height, 0.05f);
                fieldMat = EnergyFieldMaterial();
                fieldLease.OwnedResource = fieldMat;
                fieldGo.GetComponent<Renderer>().sharedMaterial = fieldMat;
                field = fieldGo.transform;
            }

            var door = new Door
            {
                Go = go,
                Pivot = pivot,
                PanelA = a,
                PanelB = b,
                Collider = col,
                Kind = nd.Kind,
                World = new Vector3(nd.X, nd.Y, nd.Z),
                Width = w,
                AxisX = nd.AxisX,
                Open = nd.Open,
                Anim = nd.Open ? 1f : 0f,
                Field = field,
                FieldMat = fieldMat,
            };
            Animate(door);
            col.enabled = !HasPassageClearance(door.Kind, w, door.Anim);
            return door;
        }

        private static readonly int FieldColorId = Shader.PropertyToID("_Color");
        private static Shader _fieldShader;

        /// <summary>A translucent, glowing blue energy-field material (item 35). Reuses the always-included
        /// <c>BlocksBeyondTheStars/Cloud</c> alpha-blend shader (no texture → a solid tinted quad), so it can't get
        /// stripped from the build into pink. Alpha is driven per-frame in <see cref="Animate"/>.</summary>
        private static Material EnergyFieldMaterial()
        {
            if (_fieldShader == null)
            {
                _fieldShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            }

            var mat = new Material(_fieldShader);
            mat.SetColor(FieldColorId, ShaderColor.Srgb(new Color(0.35f, 0.80f, 1f, 0f))); // alpha set each frame from the open amount
            mat.renderQueue = 3000; // transparent
            return mat;
        }

        /// <summary>Conservative center-passage width for the existing player capsule plus skin padding.</summary>
        internal static bool HasPassageClearance(string kind, float width, float animation)
        {
            const float requiredWidth = 0.78f; // WorldRig: 2 * (radius 0.35 + skin 0.03), plus 2 cm clearance.
            float w = Mathf.Max(1f, width);
            float t = Mathf.Clamp01(animation);
            float opening;
            if (IsHinged(kind))
            {
                float angle = t * 96f * Mathf.Deg2Rad;
                // Past 90 degrees the leaf is outside the aperture, while its thickness still protrudes.
                // The leaf ends at 98% of the aperture from its pivot, including its 2% hinge offset.
                // Its handles reach 0.1675 m from the panel plane, so include that visible thickness.
                float leafProjection = Mathf.Max(0f, w * 0.98f * Mathf.Cos(angle)) + 0.17f * Mathf.Sin(angle);
                opening = w - leafProjection;
            }
            else
            {
                opening = Mathf.Min(w - 0.01f, w * (0.01f + 0.92f * t));
            }

            return opening >= requiredWidth;
        }

        private static GameObject BuildPanel(Transform parent, float width, bool wood, bool hinge)
        {
            string key = "door:panel:" + width.ToString("R", CultureInfo.InvariantCulture) + ":" + wood + ":" + hinge;
            return EquipmentGeometry.Create(parent, "DoorPanel", key, Color.white, b =>
            {
                b.Box(Vector3.zero, new Vector3(width, Height, Thickness),
                    wood ? EquipmentGeometry.Finish.Fabric : EquipmentGeometry.Finish.Graphite, 0.020f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = side * (Thickness * 0.5f + 0.004f);
                    var front = side < 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                    if (wood)
                    {
                        for (int i = 1; i < 4; i++)
                        {
                            b.Box(new Vector3(-width * 0.5f + width * i / 4f, 0f, z), new Vector3(0.012f, Height - 0.12f, 0.01f), EquipmentGeometry.Finish.Rubber, 0.002f);
                        }
                    }
                    else
                    {
                        b.Box(new Vector3(0f, 0.38f, z), new Vector3(width - 0.055f, 1.77f, 0.025f), EquipmentGeometry.Finish.Ceramic, 0.009f);
                        b.Box(new Vector3(0f, -0.965f, z), new Vector3(width - 0.055f, 0.70f, 0.025f), EquipmentGeometry.Finish.Ceramic, 0.009f);
                        b.Box(new Vector3(0f, -0.52f, z), new Vector3(width - 0.075f, 0.085f, 0.030f), EquipmentGeometry.Finish.Steel, 0.010f);
                        b.Signal(new Vector3(-width * 0.32f, 0.48f, side * 0.109f), new Vector2(0.010f, 0.76f), new Color(0.36f, 0.80f, 0.91f), front);
                        b.Box(new Vector3(width * 0.23f, -1.03f, side * 0.110f), new Vector3(width * 0.14f, 0.17f, 0.008f), EquipmentGeometry.Finish.Orange, 0.003f);
                    }

                    if (hinge)
                    {
                        b.Box(new Vector3(width * 0.34f, -0.10f, side * 0.14f), new Vector3(0.045f, 0.21f, 0.055f), EquipmentGeometry.Finish.Graphite, 0.012f);
                    }
                }
            });
        }

        private static void BuildFrame(Transform parent, float width, bool wood)
        {
            string key = "door:frame:" + width.ToString("R", CultureInfo.InvariantCulture) + ":" + wood;
            EquipmentGeometry.Create(parent, "AirlockFrame", key, Color.white, b =>
            {
                var finish = wood ? EquipmentGeometry.Finish.Fabric : EquipmentGeometry.Finish.Graphite;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * (width * 0.5f + 0.046f);
                    b.Box(new Vector3(x, Height * 0.5f, 0f), new Vector3(0.10f, Height + 0.10f, 0.29f), finish, 0.017f);
                    if (!wood)
                    {
                        b.Box(new Vector3(x, Height * 0.5f, -0.153f), new Vector3(0.062f, Height - 0.04f, 0.020f), EquipmentGeometry.Finish.Ceramic, 0.008f);
                        b.Box(new Vector3(x, 0.18f, -0.17f), new Vector3(0.075f, 0.17f, 0.019f), EquipmentGeometry.Finish.Orange, 0.008f);
                    }
                }

                b.Box(new Vector3(0f, Height + 0.063f, 0f), new Vector3(width + 0.19f, 0.12f, 0.31f), finish, 0.023f);
                if (!wood)
                {
                    b.Signal(new Vector3(0f, Height + 0.052f, -0.158f), new Vector2(Mathf.Min(width * 0.38f, 0.70f), 0.013f), new Color(0.94f, 0.63f, 0.31f));
                }
            });
        }

        private static void DestroyDoor(Door door)
        {
            if (door.Go != null)
            {
                // Stop the stale collider immediately while Unity waits to destroy the old object.
                door.Go.SetActive(false);
                EquipmentGeometry.DestroyResource(door.Go);
            }
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = false;
                EquipmentGeometry.DestroyResource(c);
            }
        }

        private void PlayDoorSfx(Door d)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            var at = d.Go.transform.position + Vector3.up * 1f;
            string id = IsHinged(d.Kind) ? "door_hinge" : (d.Open ? "door_slide_open" : "door_slide_close");
            audio.At(id, at, 1f, 0.85f);
        }

        /// <summary>The nearest hinge door within reach of a point (for the player's E-toggle), or 0 if none.</summary>
        public int NearestHinge(Vector3 worldPos, float reach)
        {
            int best = 0;
            float bestSq = reach * reach;
            foreach (var kv in _doors)
            {
                var d = kv.Value;
                if (!IsHinged(d.Kind))
                {
                    continue;
                }

                // Compare in SCENE space (seam-aware): the door's stored position is a raw world position, but
                // the caller's position is a scene position — on a longitude-wrapped world they differ by the
                // wrap offset, which made the door read as far away (so E never opened it). (B?)
                Vector3 doorScene = Game != null ? Game.ScenePos(d.World.x, d.World.y, d.World.z) : d.World;
                float sq = (doorScene - worldPos).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = kv.Key;
                }
            }

            return best;
        }

        private void LateUpdate()
        {
            // Show an "E" hint over a hinge door the player can reach.
            var cam = Camera.main;
            if (cam == null || Game == null)
            {
                return;
            }

            int near = NearestHinge(Game.PlayerPosition, 3f);
            if (near != 0 && _doors.TryGetValue(near, out var d))
            {
                string hint = Game.Localizer != null ? Game.Localizer.Get("ui.door.hint") : "E: Door";
                ScreenLabelLayer.Instance.World(cam, d.Go.transform.position + Vector3.up * 2.2f, hint, UiKit.Cyan);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.DoorsReceived -= OnDoors;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
