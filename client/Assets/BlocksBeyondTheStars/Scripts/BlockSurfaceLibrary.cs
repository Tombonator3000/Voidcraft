// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Authored procedural surfaces for the Voidcraft concept palette. Colour, physical relief,
    /// roughness and light apertures are independent signals: painted marks never become bumps.
    /// Coordinates are local to one buildable block; no world or gameplay state is changed here.
    /// </summary>
    public static class BlockSurfaceLibrary
    {
        public static bool Contains(string key) => key is
            "basalt" or "stone" or "deepslate" or "granite" or "obsidian" or "rune_stone"
            or "iron_wall" or "steel_floor" or "metal_panel" or "engine_panel" or "lab_panel"
            or "medbay_panel" or "cargo_floor" or "ice" or "crystal" or "data_cache"
            or "dirt" or "mud" or "sand" or "snow" or "grass" or "flora_glowvine"
            or "strip_light_cyan" or "strip_light_warm" or "light_white" or "light_red" or "light_green";

        public struct Surface
        {
            public Color Albedo;
            public float Height, Roughness, Metallic, Emission;
        }

        public static Surface Sample(string key, float u, float v)
        {
            var surface = SampleOpaque(key, u, v);
            // These authored surfaces are solid. Unity's Color multiplication also scales alpha;
            // dark seams and grain must change reflectance without creating transparency or cutouts.
            // Water, glass and foliage cutouts retain their separate atlas authoring paths.
            surface.Albedo.a = 1f;
            return surface;
        }

        private static Surface SampleOpaque(string key, float u, float v)
        {
            float broad = Noise(u * 4f, v * 4f, 17f);
            float grain = Noise(u * 55f, v * 55f, 39f);
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            var s = new Surface { Height = 0.65f, Roughness = 0.76f, Emission = 1f };
            if (key == "flora_glowvine")
            {
                // Preserve the world's per-species hue in the shader. The stem has quiet organic relief;
                // only sparse bioluminescent channels emit, rather than every fleck across a bright tile.
                float channel = Mathf.Min(
                    Mathf.Abs(u - 0.30f - Mathf.Sin(v * Mathf.PI * 2f) * 0.06f),
                    Mathf.Abs(u - 0.72f - Mathf.Sin(v * Mathf.PI * 2f + 1.7f) * 0.045f));
                float aperture = 1f - Transition(0.009f, 0.018f, channel);
                s.Albedo = Color.Lerp(new Color(0.27f, 0.30f, 0.28f) * (0.94f + broad * 0.09f),
                    new Color(0.63f, 0.70f, 0.65f), aperture);
                s.Height = 0.62f + broad * 0.035f + grain * 0.012f - aperture * 0.012f;
                s.Roughness = 0.71f + grain * 0.12f;
                s.Emission = aperture * 0.80f;
                return s;
            }
            if (key is "dirt" or "mud" or "sand" or "snow" or "grass")
            {
                Color soil = key switch
                {
                    "dirt" => new Color(0.34f, 0.29f, 0.23f),
                    "mud" => new Color(0.25f, 0.24f, 0.21f),
                    "sand" => new Color(0.66f, 0.57f, 0.43f),
                    "snow" => new Color(0.77f, 0.81f, 0.83f),
                    _ => new Color(0.28f, 0.35f, 0.24f),
                };
                float fine = Noise(u * 30f, v * 30f, 61f);
                s.Albedo = soil * (0.91f + broad * 0.10f + fine * 0.045f);
                s.Height = 0.61f + broad * 0.045f + fine * 0.022f;
                s.Roughness = key == "mud" ? 0.58f + fine * 0.13f : 0.87f + fine * 0.09f;
                return s;
            }
            bool rock = key is "stone" or "basalt" or "deepslate" or "granite" or "obsidian" or "rune_stone";
            if (rock)
            {
                Color baseColor = key switch
                {
                    "stone" => new Color(0.40f, 0.43f, 0.50f),
                    "granite" => new Color(0.46f, 0.39f, 0.35f),
                    "obsidian" => new Color(0.15f, 0.17f, 0.21f),
                    "rune_stone" => new Color(0.21f, 0.25f, 0.28f),
                    "deepslate" => new Color(0.25f, 0.27f, 0.30f),
                    _ => new Color(0.22f, 0.25f, 0.31f),
                };
                float strata = Mathf.Sin((v * 7f + Noise(u * 3f, v * 2f, 4f) * 0.24f) * Mathf.PI);
                float crack = Mathf.Abs(Mathf.Sin(u * 9f + v * 3f + broad * 1.5f));
                float fissure = 1f - Transition(0.018f, 0.065f, crack);
                s.Albedo = baseColor * (0.87f + broad * 0.19f + grain * 0.06f + strata * 0.022f - fissure * 0.18f);
                s.Height = 0.58f + broad * 0.12f + grain * 0.05f + strata * 0.025f - fissure * 0.16f;
                s.Roughness = key == "obsidian" ? 0.28f + grain * 0.1f : 0.79f + grain * 0.13f;
                if (key == "rune_stone")
                {
                    float diamond = Mathf.Abs(Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) - 0.25f);
                    bool groove = diamond < 0.018f || (Mathf.Abs(u - 0.5f) < 0.012f && (v < 0.25f || v > 0.75f));
                    if (groove)
                    {
                        s.Albedo = new Color(0.13f, 0.72f, 0.82f);
                        s.Height = 0.32f;
                        s.Roughness = 0.37f;
                    }
                    s.Emission = groove ? 1f : 0.025f;
                }
                return s;
            }

            if (key is "ice" or "crystal")
            {
                bool crystal = key == "crystal";
                float facet = Noise(Mathf.Floor(u * 6f), Mathf.Floor(v * 6f), 28f);
                float vein = 1f - Transition(0.006f, 0.024f,
                    Mathf.Abs(v - 0.28f - u * 0.41f - broad * 0.035f));
                s.Albedo = Color.Lerp(new Color(0.22f, 0.35f, 0.44f),
                    crystal ? new Color(0.26f, 0.40f, 0.57f) : new Color(0.72f, 0.83f, 0.85f),
                    0.5f + broad * 0.23f + facet * 0.09f);
                s.Albedo = Color.Lerp(s.Albedo, new Color(0.42f, 0.70f, 0.84f), vein * 0.42f);
                s.Height = 0.62f + broad * 0.025f - vein * 0.055f;
                s.Roughness = 0.22f + broad * 0.13f;
                s.Emission = crystal ? 0.02f + vein * 0.24f : 1f;
                return s;
            }

            bool strip = key.StartsWith("strip_light", System.StringComparison.Ordinal);
            bool light = key.StartsWith("light_", System.StringComparison.Ordinal);
            bool floor = key is "steel_floor" or "cargo_floor";
            bool cache = key == "data_cache";
            bool dark = floor || strip || light || cache || key is "engine_panel" or "metal_panel";
            var ceramic = new Color(0.69f, 0.68f, 0.63f);
            var graphite = new Color(0.20f, 0.23f, 0.27f);
            s.Albedo = (dark ? graphite : ceramic) * (0.96f + grain * 0.035f + broad * 0.035f);
            s.Roughness = cache ? 0.66f + grain * 0.08f : dark ? 0.52f + grain * 0.12f : 0.57f + grain * 0.07f;
            s.Metallic = cache ? 0.12f : dark ? 0.72f : 0.08f;
            float seam = 1f - Transition(0.012f, 0.027f, edge);
            s.Albedo = Color.Lerp(s.Albedo, graphite * 0.52f, seam);
            s.Height -= seam * 0.18f;

            // Four recessed fasteners, with quiet broad panels between them.
            float bx = Mathf.Min(Mathf.Abs(u - 0.065f), Mathf.Abs(u - 0.935f));
            float by = Mathf.Min(Mathf.Abs(v - 0.065f), Mathf.Abs(v - 0.935f));
            float bolt = 1f - Transition(0.009f, 0.015f, Mathf.Sqrt(bx * bx + by * by));
            s.Albedo = Color.Lerp(s.Albedo, new Color(0.34f, 0.36f, 0.39f), bolt);
            s.Height -= bolt * 0.08f;
            s.Metallic = Mathf.Lerp(s.Metallic, 0.85f, bolt);

            if (floor)
            {
                float rib = 1f - Transition(0.006f, 0.012f, Mathf.Abs(Mathf.Repeat(v * 6f, 1f) - 0.5f) / 6f);
                s.Height -= rib * 0.025f;
                s.Albedo *= 1f - rib * 0.05f;
                // Safety paint is paint, not deeply embossed surface relief.
                if (key == "cargo_floor" && u > 0.78f && u < 0.90f && edge > 0.04f)
                    s.Albedo = Mathf.Repeat((u + v) * 7f, 1f) > 0.5f
                        ? new Color(0.72f, 0.40f, 0.11f) : graphite;
            }
            if (key == "engine_panel" && u > 0.82f && u < 0.88f && edge > 0.04f)
                s.Albedo = new Color(0.72f, 0.37f, 0.10f);
            if (key == "lab_panel" && v > 0.78f && v < 0.80f && u > 0.09f && u < 0.91f)
                s.Albedo = new Color(0.18f, 0.44f, 0.52f);
            if (key == "medbay_panel" && ((Mathf.Abs(u - 0.5f) < 0.035f && Mathf.Abs(v - 0.5f) < 0.14f)
                || (Mathf.Abs(v - 0.5f) < 0.035f && Mathf.Abs(u - 0.5f) < 0.14f)))
                s.Albedo = new Color(0.18f, 0.44f, 0.54f);

            if (cache)
            {
                // This same solid machine block supports the cockpit fixture. Its legacy dense grid
                // emitted across the whole pedestal; keep a readable housing around one status aperture.
                // Ceramic side rails are paint: they do not add a false ridge to the physical relief.
                if ((u > 0.085f && u < 0.14f || u > 0.86f && u < 0.915f) && v > 0.12f && v < 0.88f)
                {
                    s.Albedo = ceramic * (0.93f + broad * 0.04f);
                    s.Metallic = 0.05f;
                }
                bool bezel = Mathf.Abs(u - 0.5f) < 0.215f && Mathf.Abs(v - 0.64f) < 0.044f;
                bool aperture = Mathf.Abs(u - 0.5f) < 0.19f && Mathf.Abs(v - 0.64f) < 0.019f;
                if (bezel)
                {
                    s.Albedo = graphite * 0.38f;
                    s.Height = 0.56f;
                }
                if (aperture)
                {
                    s.Albedo = new Color(0.24f, 0.64f, 0.73f);
                    s.Height = 0.58f;
                    s.Roughness = 0.37f;
                    s.Metallic = 0f;
                }
                s.Emission = aperture ? 0.7f : 0f;
            }

            if (strip || light)
            {
                bool aperture = strip ? Mathf.Abs(v - 0.5f) < 0.045f && u > 0.07f && u < 0.93f
                    : Mathf.Abs(u - 0.5f) < 0.25f && Mathf.Abs(v - 0.5f) < 0.11f;
                if (aperture)
                {
                    s.Albedo = key switch
                    {
                        "strip_light_warm" or "light_white" => new Color(0.95f, 0.72f, 0.41f),
                        "light_red" => new Color(0.95f, 0.25f, 0.17f),
                        "light_green" => new Color(0.26f, 0.86f, 0.46f),
                        _ => new Color(0.34f, 0.79f, 0.87f),
                    };
                    s.Height = 0.59f;
                    s.Roughness = 0.36f;
                    s.Metallic = 0f;
                }
                s.Emission = aperture ? 1f : 0f;
            }
            return s;
        }

        private static float Transition(float edge0, float edge1, float value)
        {
            float t = Mathf.InverseLerp(edge0, edge1, value);
            return t * t * (3f - 2f * t);
        }

        private static float Noise(float x, float y, float seed)
            => Mathf.PerlinNoise(x + seed, y + seed * 0.73f);
    }
}
