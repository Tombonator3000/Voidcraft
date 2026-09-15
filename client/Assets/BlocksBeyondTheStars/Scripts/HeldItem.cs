// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds compact modular equipment (the tool/weapon/block currently selected) with shared cached
    /// chamfered meshes and inset displays, shared by the third-person avatar hand (<see cref="PlayerAvatar.SetHeldItem"/>) and the
    /// first-person <see cref="Viewmodel"/>. The mesh points along +Z (forward) from its holder origin.
    /// </summary>
    public static class HeldItem
    {
        public enum Kind { None, Block, Drill, Gun, Blade, Scanner, Tool, Gadget }

        /// <summary>Resolves a block key to its atlas texture + tile UV rect, so a held block shows its REAL
        /// in-world texture instead of a flat map colour. Wired by GameBootstrap once the atlas exists; null
        /// (or a null result) falls back to the tinted cube.</summary>
        public static System.Func<string, (Texture2D Tex, Rect Uv)?> BlockTileResolver;

        /// <summary>Maps the selected inventory item to a held-item kind + tint (+ the block key for
        /// <see cref="Kind.Block"/>, so the held cube can carry the block's real atlas tile).</summary>
        public static (Kind kind, Color tint, string blockKey) For(GameContent content, string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey) || content == null)
            {
                return (Kind.None, Color.white, null);
            }

            var def = content.GetItem(itemKey);
            if (def == null)
            {
                return (Kind.None, Color.white, null);
            }

            if (!string.IsNullOrEmpty(def.PlacesBlock))
            {
                return (Kind.Block, WorldMap.MapColor(def.PlacesBlock), def.PlacesBlock);
            }

            var tool = def.Tool;
            if (tool == null)
            {
                return (Kind.None, Color.white, null); // raw material — nothing meaningful to hold up
            }

            switch (tool.Kind)
            {
                case ToolKind.Drill: return (Kind.Drill, new Color(0.62f, 0.66f, 0.72f), null);
                case ToolKind.Scanner: return (Kind.Scanner, new Color(0.45f, 0.85f, 0.95f), null);
                case ToolKind.Weapon: return IsRanged(itemKey) ? (Kind.Gun, GunTint(itemKey), null) : (Kind.Blade, new Color(0.80f, 0.84f, 0.90f), null);
                case ToolKind.Gadget: return (Kind.Gadget, GadgetTint(itemKey), null);
                default: return (Kind.Tool, new Color(0.60f, 0.62f, 0.66f), null);
            }
        }

        /// <summary>The emitter glow colour for a gadget's held model (item 36).</summary>
        private static Color GadgetTint(string key)
        {
            if (key.Contains("medkit")) return new Color(0.35f, 1f, 0.55f);   // green first-aid
            if (key.Contains("stasis")) return new Color(0.4f, 0.8f, 1f);     // cyan stasis
            if (key.Contains("blaster")) return new Color(1f, 0.55f, 0.25f);  // orange blast
            return new Color(0.6f, 0.85f, 0.9f);
        }

        private static bool IsRanged(string key)
            => key.Contains("pistol") || key.Contains("blaster") || key.Contains("gauss")
               || key.Contains("laser") || key.Contains("plasma") || key.Contains("cannon") || key.Contains("gun");

        private static Color GunTint(string key)
        {
            if (key.Contains("plasma")) return new Color(0.85f, 0.5f, 1f);
            if (key.Contains("laser")) return new Color(1f, 0.5f, 0.45f);
            if (key.Contains("gauss")) return new Color(0.55f, 0.9f, 1f);
            return new Color(0.5f, 0.54f, 0.6f);
        }

        /// <summary>Builds the held-item geometry under a new holder parented to <paramref name="parent"/>.
        /// For blocks, <paramref name="blockKey"/> lets the cube carry its REAL atlas tile (textured hand
        /// block instead of a flat colour); without a resolver/tile it falls back to the tint.</summary>
        public static GameObject Build(Transform parent, Kind kind, Color tint, string blockKey = null)
        {
            if (kind == Kind.None)
            {
                return null;
            }

            if (kind == Kind.Block)
            {
                return BuildBlock(parent, tint, blockKey);
            }

            // First-person and remote-player equipment share both meshes and materials. A lease releases
            // them after the final hand/viewmodel stops using the item, including scene/world teardown.
            string key = "held:" + kind + ":" + ColorUtility.ToHtmlStringRGBA(tint);
            return EquipmentGeometry.Create(parent, "Held", key, tint, builder =>
            {
                switch (kind)
                {
                    case Kind.Scanner: BuildScanner(builder, tint); break;
                    case Kind.Drill: BuildDrill(builder); break;
                    case Kind.Gun: BuildEmitter(builder, tint, true); break;
                    case Kind.Gadget: BuildEmitter(builder, tint, false); break;
                    case Kind.Blade:
                        builder.Box(new Vector3(0f, -0.04f, 0f), new Vector3(0.07f, 0.07f, 0.16f), EquipmentGeometry.Finish.Rubber, 0.01f);
                        builder.Box(new Vector3(0f, 0.02f, 0.12f), new Vector3(0.10f, 0.10f, 0.025f), EquipmentGeometry.Finish.Graphite, 0.008f);
                        builder.Box(new Vector3(0f, 0.02f, 0.28f), new Vector3(0.025f, 0.15f, 0.32f), EquipmentGeometry.Finish.Steel, 0.009f);
                        break;
                    default:
                        builder.Box(new Vector3(0f, 0f, 0.06f), new Vector3(0.12f, 0.12f, 0.26f), EquipmentGeometry.Finish.Graphite, 0.02f);
                        builder.Box(new Vector3(0f, 0.01f, 0.10f), new Vector3(0.125f, 0.095f, 0.13f), EquipmentGeometry.Finish.Ceramic, 0.012f);
                        break;
                }
            });
        }

        private static GameObject BuildBlock(Transform parent, Color tint, string blockKey)
        {
            var holder = new GameObject("Held");
            var lease = holder.AddComponent<EquipmentGeometryLease>();
            holder.transform.SetParent(parent, false);
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "AtlasBlock";
            var collider = block.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                EquipmentGeometry.DestroyResource(collider);
            }

            block.transform.SetParent(holder.transform, false);
            block.transform.localPosition = new Vector3(0f, 0f, 0.16f);
            block.transform.localScale = Vector3.one * 0.22f;
            var material = new Material(Shader.Find("BlocksBeyondTheStars/LitColor")) { color = ShaderColor.Srgb(tint) };
            if (blockKey != null && BlockTileResolver?.Invoke(blockKey) is { } tile)
            {
                material.color = Color.white;
                material.mainTexture = tile.Tex;
                material.mainTextureScale = new Vector2(tile.Uv.width, tile.Uv.height);
                material.mainTextureOffset = new Vector2(tile.Uv.x, tile.Uv.y);
            }

            block.GetComponent<Renderer>().sharedMaterial = material;
            lease.OwnedResource = material;
            return holder;
        }

        /// <summary>A first-person-only glove wrapped around the grip; remote avatars already own hands.</summary>
        internal static GameObject BuildGripHand(Transform parent, Kind kind, Color suitColor)
        {
            if (kind != Kind.Drill && kind != Kind.Scanner && kind != Kind.Gadget && kind != Kind.Gun && kind != Kind.Tool)
            {
                return null;
            }

            string key = "grip-hand:" + kind + ":" + ColorUtility.ToHtmlStringRGBA(suitColor);
            var hand = EquipmentGeometry.Create(parent, "SurveyGlove", key, suitColor, b =>
            {
                Vector3 grip = kind == Kind.Scanner ? new Vector3(0f, -0.105f, 0.023f) : new Vector3(0f, -0.12f, -0.025f);
                b.Box(grip + new Vector3(0.024f, -0.012f, -0.048f), new Vector3(0.115f, 0.117f, 0.078f), EquipmentGeometry.Finish.Rubber, 0.019f);
                b.Box(grip + new Vector3(0.045f, -0.015f, -0.065f), new Vector3(0.070f, 0.099f, 0.036f), EquipmentGeometry.Finish.Graphite, 0.010f);
                for (int i = 0; i < 4; i++)
                {
                    // Separate padded fingers bend around the near side of the existing tool grip.
                    b.Box(grip + new Vector3(-0.014f, 0.039f - i * 0.028f, -0.054f), new Vector3(0.092f, 0.022f, 0.028f),
                        EquipmentGeometry.Finish.Graphite, 0.008f, Quaternion.Euler(0f, -9f, -5f));
                    b.Box(grip + new Vector3(-0.060f, 0.035f - i * 0.028f, -0.029f), new Vector3(0.026f, 0.022f, 0.062f),
                        EquipmentGeometry.Finish.Rubber, 0.007f);
                }

                b.Box(grip + new Vector3(0.057f, 0.052f, -0.013f), new Vector3(0.041f, 0.087f, 0.046f),
                    EquipmentGeometry.Finish.Graphite, 0.014f, Quaternion.Euler(-20f, 0f, -27f));
                b.Box(grip + new Vector3(0.021f, -0.10f, -0.049f), new Vector3(0.12f, 0.072f, 0.116f), EquipmentGeometry.Finish.Tint, 0.018f);
                b.Box(grip + new Vector3(0.021f, -0.12f, -0.049f), new Vector3(0.127f, 0.023f, 0.123f), EquipmentGeometry.Finish.Graphite, 0.009f);
                b.Box(grip + new Vector3(0.035f, -0.19f, -0.043f), new Vector3(0.127f, 0.14f, 0.131f), EquipmentGeometry.Finish.Tint, 0.025f,
                    Quaternion.Euler(-8f, 0f, 9f));
            });
            hand.transform.localScale = Vector3.one * 0.9f;
            return hand;
        }

        private static void BuildScanner(EquipmentGeometry.Builder b, Color signal)
        {
            b.Box(new Vector3(0f, 0.045f, 0.045f), new Vector3(0.20f, 0.20f, 0.19f), EquipmentGeometry.Finish.Graphite, 0.027f);
            b.Box(new Vector3(0f, 0.054f, 0.055f), new Vector3(0.211f, 0.177f, 0.15f), EquipmentGeometry.Finish.Ceramic, 0.025f);
            b.Box(new Vector3(0f, 0.061f, -0.055f), new Vector3(0.174f, 0.153f, 0.023f), EquipmentGeometry.Finish.Rubber, 0.016f);
            b.Box(new Vector3(0f, 0.066f, -0.070f), new Vector3(0.138f, 0.114f, 0.009f), EquipmentGeometry.Finish.Glass, 0.009f);
            b.Box(new Vector3(0f, -0.105f, 0.025f), new Vector3(0.080f, 0.15f, 0.087f), EquipmentGeometry.Finish.Rubber, 0.013f,
                Quaternion.Euler(-12f, 0f, 0f));
            b.Box(new Vector3(0f, -0.025f, 0.124f), new Vector3(0.132f, 0.055f, 0.028f), EquipmentGeometry.Finish.Graphite, 0.008f);
            b.Box(new Vector3(0.106f, 0.07f, 0.04f), new Vector3(0.008f, 0.065f, 0.08f), EquipmentGeometry.Finish.Orange, 0.002f);
            for (int i = 0; i < 4; i++)
            {
                b.Box(new Vector3(-0.106f, 0.022f + i * 0.022f, 0.04f), new Vector3(0.007f, 0.009f, 0.077f), EquipmentGeometry.Finish.Rubber);
                b.Box(new Vector3(0f, -0.07f - i * 0.026f, -0.023f), new Vector3(0.071f, 0.008f, 0.013f), EquipmentGeometry.Finish.Graphite, 0.003f);
            }

            // A geometric survey reticle on recessed glass, readable from the actual viewmodel camera.
            var top = new Vector3(0f, 0.107f, -0.076f);
            var right = new Vector3(0.035f, 0.073f, -0.076f);
            var bottom = new Vector3(0f, 0.039f, -0.076f);
            var left = new Vector3(-0.035f, 0.073f, -0.076f);
            b.SignalLine(top, right, 0.0022f, signal);
            b.SignalLine(right, bottom, 0.0022f, signal);
            b.SignalLine(bottom, left, 0.0022f, signal);
            b.SignalLine(left, top, 0.0022f, signal);
            b.SignalLine(new Vector3(-0.041f, 0.073f, -0.076f), new Vector3(0.041f, 0.073f, -0.076f), 0.0014f, signal * 0.65f);
            b.SignalLine(new Vector3(0f, 0.026f, -0.076f), new Vector3(0f, 0.114f, -0.076f), 0.0014f, signal * 0.65f);
            b.Signal(new Vector3(-0.039f, 0.020f, -0.076f), new Vector2(0.025f, 0.003f), signal);
            b.Signal(new Vector3(-0.084f, 0.072f, -0.068f), new Vector2(0.004f, 0.058f), signal);
            b.Signal(new Vector3(0.084f, 0.072f, -0.068f), new Vector2(0.004f, 0.058f), signal);
        }

        private static void BuildDrill(EquipmentGeometry.Builder b)
        {
            b.Box(new Vector3(0f, 0.012f, 0.027f), new Vector3(0.18f, 0.175f, 0.25f), EquipmentGeometry.Finish.Graphite, 0.025f);
            b.Box(new Vector3(0f, 0.026f, 0.002f), new Vector3(0.188f, 0.147f, 0.164f), EquipmentGeometry.Finish.Ceramic, 0.021f);
            b.Box(new Vector3(0f, -0.12f, -0.027f), new Vector3(0.081f, 0.17f, 0.092f), EquipmentGeometry.Finish.Rubber, 0.014f,
                Quaternion.Euler(-10f, 0f, 0f));
            b.Box(new Vector3(0f, -0.197f, -0.037f), new Vector3(0.102f, 0.033f, 0.117f), EquipmentGeometry.Finish.Graphite, 0.008f);
            b.Barrel(new Vector3(0f, 0.013f, 0.157f), 0.078f, 0.064f, 0.077f, EquipmentGeometry.Finish.Rubber);
            b.Barrel(new Vector3(0f, 0.013f, 0.196f), 0.068f, 0.057f, 0.03f, EquipmentGeometry.Finish.Steel);
            b.Barrel(new Vector3(0f, 0.013f, 0.252f), 0.043f, 0.027f, 0.09f, EquipmentGeometry.Finish.Graphite);
            b.Barrel(new Vector3(0f, 0.013f, 0.319f), 0.028f, 0.005f, 0.064f, EquipmentGeometry.Finish.Steel);
            for (int i = 0; i < 3; i++)
            {
                b.Box(new Vector3(0f, 0.013f, 0.222f + i * 0.03f), new Vector3(0.09f - i * 0.012f, 0.014f, 0.018f),
                    EquipmentGeometry.Finish.Steel, 0.004f, Quaternion.Euler(0f, 0f, i * 55f));
            }

            for (int i = 0; i < 4; i++)
            {
                b.Box(new Vector3(0.095f, 0.04f, -0.048f + i * 0.022f), new Vector3(0.009f, 0.045f, 0.009f), EquipmentGeometry.Finish.Rubber, 0.002f);
                b.Box(new Vector3(0f, -0.07f - i * 0.027f, -0.076f), new Vector3(0.07f, 0.009f, 0.011f), EquipmentGeometry.Finish.Graphite, 0.002f);
            }

            b.Box(new Vector3(-0.095f, 0.03f, 0.013f), new Vector3(0.008f, 0.037f, 0.082f), EquipmentGeometry.Finish.Orange, 0.003f);
            b.Box(new Vector3(0f, 0.018f, -0.101f), new Vector3(0.107f, 0.061f, 0.015f), EquipmentGeometry.Finish.Glass, 0.007f);
            b.Signal(new Vector3(0f, 0.026f, -0.110f), new Vector2(0.070f, 0.006f), new Color(0.38f, 0.78f, 0.88f));
            b.Signal(new Vector3(-0.026f, 0.01f, -0.110f), new Vector2(0.017f, 0.004f), new Color(0.94f, 0.57f, 0.24f));
            // Open trigger guard stays mechanical and compact; the existing swing supplies action motion.
            b.Box(new Vector3(0f, -0.065f, 0.055f), new Vector3(0.031f, 0.10f, 0.021f), EquipmentGeometry.Finish.Graphite, 0.006f);
            b.Box(new Vector3(0f, -0.111f, 0.021f), new Vector3(0.031f, 0.021f, 0.078f), EquipmentGeometry.Finish.Graphite, 0.006f);
        }

        private static void BuildEmitter(EquipmentGeometry.Builder b, Color signal, bool longBarrel)
        {
            b.Box(new Vector3(0f, 0f, 0.05f), new Vector3(0.14f, 0.13f, 0.21f), EquipmentGeometry.Finish.Graphite, 0.018f);
            b.Box(new Vector3(0f, 0.021f, 0.02f), new Vector3(0.148f, 0.09f, 0.13f), EquipmentGeometry.Finish.Ceramic, 0.014f);
            b.Box(new Vector3(0f, -0.115f, -0.033f), new Vector3(0.076f, 0.16f, 0.091f), EquipmentGeometry.Finish.Rubber, 0.012f);
            float length = longBarrel ? 0.19f : 0.105f;
            b.Barrel(new Vector3(0f, 0f, 0.11f + length * 0.5f), 0.046f, 0.035f, length, EquipmentGeometry.Finish.Steel);
            b.Box(new Vector3(0f, 0.012f, -0.061f), new Vector3(0.073f, 0.035f, 0.008f), EquipmentGeometry.Finish.Glass, 0.003f);
            b.Signal(new Vector3(0f, 0.012f, -0.067f), new Vector2(0.056f, 0.005f), signal);
        }
    }
}
