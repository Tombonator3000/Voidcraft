// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class BlockSurfaceLibraryEditModeTests
    {
        [Test]
        public void AuthoredOpaqueSurfacesKeepAlphaIndependentOfDarkSeamsAndGrain()
        {
            string[] keys =
            {
                "basalt", "stone", "deepslate", "granite", "obsidian", "rune_stone",
                "iron_wall", "steel_floor", "metal_panel", "engine_panel", "lab_panel",
                "medbay_panel", "cargo_floor", "ice", "crystal", "data_cache",
                "dirt", "mud", "sand", "snow", "grass", "flora_glowvine",
                "strip_light_cyan", "strip_light_warm", "light_white", "light_red", "light_green",
            };
            // Tile edges, recessed fasteners, painted rails, organic channels and light apertures
            // exercise every authored branch, including the dark cockpit bezel below the cutout limit.
            float[] coordinates = { 0f, 0.003f, 0.065f, 0.11f, 0.30f, 0.4f, 0.5f, 0.61f, 0.64f, 0.935f, 1f };
            foreach (string key in keys)
            {
                Assert.IsTrue(BlockSurfaceLibrary.Contains(key));
                foreach (float u in coordinates)
                foreach (float v in coordinates)
                    Assert.AreEqual(1f, BlockSurfaceLibrary.Sample(key, u, v).Albedo.a,
                        $"{key} at ({u}, {v}): RGB shading must not change opacity.");
            }
            Assert.IsFalse(BlockSurfaceLibrary.Contains("water"), "Water keeps its explicit atlas transparency.");
            Assert.IsFalse(BlockSurfaceLibrary.Contains("glass"), "Glass keeps its separate transparent rendering contract.");
            Assert.IsFalse(BlockSurfaceLibrary.Contains("flora_fern"), "Foliage cutouts keep their authored asset alpha.");
        }

        [Test]
        public void CeramicPanelKeepsQuietBroadSurfaceBetweenRecessedSeams()
        {
            var centre = BlockSurfaceLibrary.Sample("iron_wall", 0.5f, 0.5f);
            var seam = BlockSurfaceLibrary.Sample("iron_wall", 0.003f, 0.5f);
            Assert.Greater(centre.Height - seam.Height, 0.1f);
            Assert.Greater(centre.Albedo.grayscale - seam.Albedo.grayscale, 0.3f);
            Assert.Less(centre.Metallic, 0.2f, "Ceramic paint is dielectric, not bare polished metal.");
        }

        [Test]
        public void PaintedCargoStripeDoesNotCreateCollisionLookingRelief()
        {
            var plain = BlockSurfaceLibrary.Sample("steel_floor", 0.84f, 0.55f);
            var painted = BlockSurfaceLibrary.Sample("cargo_floor", 0.84f, 0.55f);
            Assert.AreEqual(plain.Height, painted.Height);
            Assert.AreNotEqual(plain.Albedo, painted.Albedo);
        }

        [Test]
        public void CockpitCacheKeepsOpaqueHousingAroundOneSmallRecessedAperture()
        {
            Assert.IsTrue(BlockSurfaceLibrary.Contains("data_cache"),
                "The atlas must use the authored surface instead of its legacy emissive grid.");
            var housing = BlockSurfaceLibrary.Sample("data_cache", 0.5f, 0.4f);
            var aperture = BlockSurfaceLibrary.Sample("data_cache", 0.5f, 0.64f);
            var paintedRail = BlockSurfaceLibrary.Sample("data_cache", 0.11f, 0.4f);
            Assert.AreEqual(0f, housing.Emission);
            Assert.AreEqual(0f, paintedRail.Emission);
            Assert.Greater(housing.Roughness, 0.6f);
            Assert.Less(housing.Metallic, 0.2f);
            Assert.Less(aperture.Height, housing.Height, "Status light must sit inside the solid housing.");
            Assert.Greater(aperture.Emission, 0.5f);
            Assert.Less(aperture.Emission, 0.8f);

            int lit = 0;
            const int resolution = 64;
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                var sample = BlockSurfaceLibrary.Sample("data_cache", (x + 0.5f) / resolution, (y + 0.5f) / resolution);
                Assert.AreEqual(1f, sample.Albedo.a, "The machine pedestal must remain opaque.");
                if (sample.Emission > 0.5f) lit++;
            }
            Assert.Greater(lit, resolution * resolution * 0.005f);
            Assert.Less(lit, resolution * resolution * 0.025f,
                "A full-size cockpit marker must not turn into a large luminous pedestal again.");
        }

        [TestCase("light_white")]
        [TestCase("strip_light_cyan")]
        [TestCase("strip_light_warm")]
        [TestCase("flora_glowvine")]
        public void AuthoredEmissionIsLimitedToItsAperture(string key)
        {
            int lit = 0;
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    if (BlockSurfaceLibrary.Sample(key, (x + 0.5f) / 32, (y + 0.5f) / 32).Emission > 0.5f) lit++;
                }
            }
            Assert.Greater(lit, 0);
            Assert.Less(lit, 32 * 32 / 4, "Most of the surface must remain visible outside its emissive aperture.");
        }
    }
}
