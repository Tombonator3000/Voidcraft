// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>Exercises the atlas-enabled mesher: atlas-null builds never entered the broken bevel path.</summary>
    public sealed class TerrainSeamPlayModeTests
    {
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };
        private GameContent _content;
        private BlockTextureAtlas _atlas;

        [OneTimeSetUp]
        public void LoadRealAtlas()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            Assert.IsTrue(File.Exists(Path.Combine(dataDir, "blocks.json")),
                "Synced game content is required to verify the actual terrain mesher.");
            _content = ContentLoader.LoadFromDirectory(dataDir);
            _atlas = new BlockTextureAtlas(_content);
        }

        [OneTimeTearDown]
        public void ReleaseAtlas() => _atlas?.Destroy();

        [TestCase("dirt", false)]
        [TestCase("grass", false)]
        [TestCase("stone", false)]
        [TestCase("stone", true)]
        [TestCase("mixed", false)]
        [TestCase("mixed", true)]
        public void LShapedTerrainHasCompleteSealedFaces(string key, bool crossChunkBoundary)
        {
            // A's front is exposed; B's front is blocked by C. Previously A's chamfer ended at
            // A/B's culled shared face and left an uncapped triangular window into the terrain.
            var a = new Vector3Int(crossChunkBoundary ? WorldConstants.ChunkSize - 1 : 8, 8, 8);
            var cells = new Dictionary<Vector3Int, string>
            {
                [a] = key == "mixed" ? "stone" : key,
                [a + Vector3Int.right] = key == "mixed" ? "grass" : key,
                [a + new Vector3Int(1, 0, -1)] = key == "mixed" ? "iron_ore" : key,
            };
            AssertFullSealedFaces(cells, Build(cells));
        }

        [Test]
        public void SteppedSoilHasNoInsetFacesOrArtificialCaps()
        {
            var cells = new Dictionary<Vector3Int, string>();
            for (int x = 0; x < 3; x++)
            for (int y = 0; y <= x; y++)
            {
                cells[new Vector3Int(8 + x, 7 + y, 8)] = y == x ? "grass" : "dirt";
            }
            AssertFullSealedFaces(cells, Build(cells));
        }

        [Test]
        public void ManufacturedHullKeepsChamfersAndFullCubeCollision()
        {
            var cells = new Dictionary<Vector3Int, string> { [new Vector3Int(8, 8, 8)] = "iron_wall" };
            var mesh = Build(cells);
            Assert.IsTrue(mesh.Vertices.Exists(v =>
                (v - (Vector3)Vector3Int.RoundToInt(v)).sqrMagnitude > 0.000001f),
                "The terrain correction must not remove manufactured hull chamfers.");
        }

        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
        }

        private Geometry Build(Dictionary<Vector3Int, string> cells)
        {
            var world = new Dictionary<Vector3Int, BlockId>();
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            foreach (var cell in cells)
            {
                var id = _content.GetBlock(cell.Value).NumericId;
                world[cell.Key] = id;
                var pos = new Vector3i(cell.Key.x, cell.Key.y, cell.Key.z);
                var coord = WorldConstants.WorldToChunk(pos);
                if (!chunks.TryGetValue(coord, out var chunk)) chunks[coord] = chunk = new ChunkData(coord);
                var local = WorldConstants.WorldToLocal(pos);
                chunk.Set(local.X, local.Y, local.Z, id);
            }
            BlockId World(int x, int y, int z)
                => world.TryGetValue(new Vector3Int(x, y, z), out var id) ? id : BlockId.Air;

            var result = new Geometry();
            foreach (var chunk in chunks.Values)
            {
                var actual = ChunkMesher.BuildGeometry(chunk, _content, World, _atlas);
                var fullCubeReference = ChunkMesher.BuildGeometry(chunk, _content, World);
                try
                {
                    CollectionAssert.AreEqual(fullCubeReference.ColliderVerts, actual.ColliderVerts,
                        "Render-only terrain changes must preserve the full-cell collision geometry.");
                    CollectionAssert.AreEqual(fullCubeReference.ColliderTris, actual.ColliderTris);
                    var origin = WorldConstants.ChunkOrigin(chunk.Coord);
                    var offset = new Vector3(origin.X, origin.Y, origin.Z);
                    int start = result.Vertices.Count;
                    foreach (var vertex in actual.Verts) result.Vertices.Add(vertex + offset);
                    foreach (int index in actual.OpaqueTris) result.Triangles.Add(start + index);
                }
                finally
                {
                    actual.Release();
                    fullCubeReference.Release();
                }
            }
            return result;
        }

        private static void AssertFullSealedFaces(Dictionary<Vector3Int, string> cells, Geometry mesh)
        {
            var faces = new Dictionary<(Vector3Int Cell, Vector3Int Normal), int>();
            foreach (var cell in cells.Keys)
            foreach (var direction in Directions)
            {
                if (!cells.ContainsKey(cell + direction)) faces[(cell, direction)] = 0;
            }
            var edges = new Dictionary<(Vector3Int A, Vector3Int B), int>();
            for (int i = 0; i < mesh.Triangles.Count; i += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[i]];
                Vector3 b = mesh.Vertices[mesh.Triangles[i + 1]];
                Vector3 c = mesh.Vertices[mesh.Triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                Assert.AreEqual(1f, cross.magnitude, 0.00001f,
                    "Each terrain triangle must cover half a complete unit face, with no bevel slivers or caps.");
                var normal = Vector3Int.RoundToInt(cross);
                Assert.AreEqual(1, Mathf.Abs(normal.x) + Mathf.Abs(normal.y) + Mathf.Abs(normal.z));
                var owner = Vector3Int.FloorToInt((a + b + c) / 3f - (Vector3)normal * 0.01f);
                Assert.IsTrue(faces.ContainsKey((owner, normal)),
                    "Only outward faces toward empty cells should be emitted; shared faces stay culled.");
                faces[(owner, normal)]++;
                AddEdge(a, b); AddEdge(b, c); AddEdge(c, a);
            }
            foreach (var face in faces)
                Assert.AreEqual(2, face.Value, "A visible unit face is missing or incomplete at " + face.Key);
            foreach (var edge in edges)
                Assert.AreEqual(2, edge.Value, "An unsealed render edge remains at " + edge.Key);

            void AddEdge(Vector3 a, Vector3 b)
            {
                var start = Vector3Int.RoundToInt(a);
                var end = Vector3Int.RoundToInt(b);
                Assert.Less((a - (Vector3)start).sqrMagnitude, 0.00000001f, "Terrain faces must meet at full cell boundaries.");
                Assert.Less((b - (Vector3)end).sqrMagnitude, 0.00000001f);
                bool reverse = start.x > end.x || (start.x == end.x &&
                    (start.y > end.y || (start.y == end.y && start.z > end.z)));
                var key = reverse ? (end, start) : (start, end);
                edges.TryGetValue(key, out int count);
                edges[key] = count + 1;
            }
        }
    }
}
