// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// PlayMode test for the Unity-coupled <see cref="BlockTextureAtlas"/>: it builds a real
    /// <see cref="Texture2D"/> atlas from the synced game content. Runs in PlayMode (not EditMode)
    /// because the atlas builder calls <c>Object.Destroy</c>, which is illegal in edit mode.
    /// Requires the content under StreamingAssets/data (run scripts/sync-client-libs.ps1 first);
    /// skipped gracefully otherwise.
    /// </summary>
    public sealed class BlockAtlasPlayModeTests
    {
        [Test]
        public void BuildsASquareAtlasTextureFromContent()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
                return;
            }

            var content = ContentLoader.LoadFromDirectory(dataDir);
            var atlas = new BlockTextureAtlas(content);

            try
            {
                Assert.IsNotNull(atlas.Texture, "Atlas texture should be created.");
                Assert.AreEqual(BlockTextureAtlas.Cols * BlockTextureAtlas.Tile, atlas.Texture.width);
                Assert.AreEqual(BlockTextureAtlas.Rows * BlockTextureAtlas.Tile, atlas.Texture.height);
                Assert.AreEqual(atlas.Texture.width, atlas.SurfaceTexture.width);

                // Safety paint changes colour without adding a physical ridge to the walkable floor.
                ushort cargo = content.GetBlock("cargo_floor").NumericId.Value;
                ushort floor = content.GetBlock("steel_floor").NumericId.Value;
                Color Cargo(Texture2D map) => Sample(map, cargo, 0.84f, 0.55f);
                Color Floor(Texture2D map) => Sample(map, floor, 0.84f, 0.55f);
                Assert.Greater(Mathf.Abs(Cargo(atlas.Texture).r - Floor(atlas.Texture).r), 0.15f);
                Assert.AreEqual(Cargo(atlas.NormalTexture), Floor(atlas.NormalTexture),
                    "Paint must not become a bump or cavity.");
                Assert.AreEqual(Cargo(atlas.SurfaceTexture).a, Floor(atlas.SurfaceTexture).a);

                ushort lamp = content.GetBlock("light_white").NumericId.Value;
                Assert.Less(Sample(atlas.SurfaceTexture, lamp, 0.1f, 0.5f).b, 0.01f,
                    "The lamp housing must not emit light.");
                Assert.Greater(Sample(atlas.SurfaceTexture, lamp, 0.5f, 0.5f).b, 0.9f);
                ushort basalt = content.GetBlock("basalt").NumericId.Value;
                Assert.IsTrue(atlas.TryGetVariants(basalt, out var variants));
                foreach (ushort tile in variants)
                {
                    Assert.AreEqual(Sample(atlas.SurfaceTexture, basalt, 0.5f, 0.5f),
                        Sample(atlas.SurfaceTexture, tile, 0.5f, 0.5f), "Variants retain material properties.");
                }
                ushort flora = content.GetBlock("flora_fern").NumericId.Value;
                Assert.AreEqual(0f, Sample(atlas.SurfaceTexture, flora, 0.5f, 0.5f).a,
                    "Legacy content must keep its vertex material fallback.");
                var pixels = atlas.Texture.GetPixels(flora % BlockTextureAtlas.Cols * BlockTextureAtlas.Tile,
                    flora / BlockTextureAtlas.Cols * BlockTextureAtlas.Tile, BlockTextureAtlas.Tile, BlockTextureAtlas.Tile);
                Assert.IsTrue(System.Array.Exists(pixels, c => c.a < 0.5f),
                    "Upscaling legacy assets must preserve foliage cutouts.");
            }
            finally
            {
                atlas.Destroy();
            }
        }

        private static Color Sample(Texture2D texture, ushort tile, float u, float v)
            => texture.GetPixel(tile % BlockTextureAtlas.Cols * BlockTextureAtlas.Tile + (int)(u * BlockTextureAtlas.Tile),
                tile / BlockTextureAtlas.Cols * BlockTextureAtlas.Tile + (int)(v * BlockTextureAtlas.Tile));
    }
}
