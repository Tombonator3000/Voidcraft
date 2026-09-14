// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Reflection;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

public sealed class VeylLandscapePlacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_veyl_landscape_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(string planet, long seed, out SqliteWorldRepository repo, bool story = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "landscape"));
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "landscape",
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { StoryId = story ? "voidcraft_awakening" : "none" },
        }, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static StructurePlacementRecord Record(SvGameServer server)
        => Assert.Single(server.PlacementRecordsForTest.Where(p => p.Kind == "veyl_survey" && p.Placed));

    private List<BlockEdit> LandscapeEdits(SvGameServer server, SqliteWorldRepository repo, StructurePlacementRecord record)
    {
        var bounds = Assert.IsType<StructureReservationBounds>(record.Reservation);
        var structure = VeylSurveyGenerator.Generate(_content, "stone", record.GeometryVersion);
        int circ = server.World.Circumference;
        var coords = new HashSet<ChunkCoord>();
        for (int x = bounds.MinX!.Value; x <= bounds.MaxX!.Value + WorldConstants.ChunkSize - 1; x += WorldConstants.ChunkSize)
            for (int y = bounds.MinY!.Value; y <= bounds.MaxY!.Value + WorldConstants.ChunkSize - 1; y += WorldConstants.ChunkSize)
                for (int z = bounds.MinZ!.Value; z <= bounds.MaxZ!.Value + WorldConstants.ChunkSize - 1; z += WorldConstants.ChunkSize)
                    coords.Add(WorldConstants.WorldToChunk(new Vector3i(WorldConstants.WrapX(x, circ), y, WorldConstants.WrapZ(z, circ))));
        bool Landscape(Vector3i p)
        {
            int x = record.X + WorldConstants.WrapDeltaX(p.X - record.X, circ);
            int z = record.Z + WorldConstants.WrapDeltaZ(p.Z - record.Z, circ);
            return x >= bounds.MinX && x <= bounds.MaxX && p.Y >= bounds.MinY && p.Y <= bounds.MaxY
                && z >= bounds.MinZ && z <= bounds.MaxZ
                && (x < record.X - 5 || x > record.X + structure.Width + 4 || z > record.Z + structure.Length + 4);
        }
        return coords.SelectMany(c => repo.LoadChunkEdits(server.World.LocationId, c))
            .Where(e => Landscape(e.WorldPosition)).OrderBy(e => e.WorldPosition.X).ThenBy(e => e.WorldPosition.Z)
            .ThenBy(e => e.WorldPosition.Y).ToList();
    }

    [Theory]
    [InlineData("rocky", 4242, "buried")]
    [InlineData("ocean", 71, "terrace_v1")]
    public void FreshDryAndFallbackSites_HaveBoundedGroundedLocalBasaltAndOpenAccess(string planet, long seed, string seat)
    {
        var server = Start(planet, seed, out var repo);
        using (repo)
        {
            var record = Record(server);
            Assert.Equal(seat, record.Seat);
            if (seat == "terrace_v1")
            {
                // An ocean body can select dry land. The seat class describes sampled local water,
                // not the planet label: independently confirm that this fallback's entry is dry.
                var generator = (WorldGenerator)typeof(SvGameServer).GetField("_generator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
                var structure = VeylSurveyGenerator.Generate(_content, "stone", record.GeometryVersion);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -3; dz <= 3; dz++)
                        Assert.False(generator.TryGetWaterSurface(server.World.Planet,
                            record.X + structure.Width / 2 + dx, record.Z + dz, out _, out _));
            }
            Assert.Equal(1, record.LandscapeVersion);
            var edits = LandscapeEdits(server, repo, record);
            Assert.InRange(edits.Count, 1, VeylBasaltLandformGenerator.AddedCellLimit);
            var basalt = _content.GetBlock("basalt")!.NumericId;
            var bounds = record.Reservation!;
            int surface = record.GroundY + VeylSurveyGenerator.BurialDepthFor(record.GeometryVersion);
            foreach (var column in edits.GroupBy(e => (e.WorldPosition.X, e.WorldPosition.Z)))
            {
                var bottom = column.MinBy(e => e.WorldPosition.Y);
                int top = column.Max(e => e.WorldPosition.Y);
                Assert.True(server.World.Definition(server.World.GetBlock(bottom.WorldPosition + new Vector3i(0, -1, 0)))?.Solid == true);
                Assert.Equal(top - bottom.WorldPosition.Y + 1, column.Count());
                Assert.InRange(column.Count(), 8, VeylBasaltLandformGenerator.SupportDepthLimit);
                Assert.True(top <= surface + 16, "Local pillars remain below the Anchor's top.");
                foreach (var edit in column)
                {
                    Assert.Equal(basalt.Value, edit.Block);
                    Assert.Equal(0, edit.Shape);
                    Assert.Equal(basalt, server.World.GetBlock(edit.WorldPosition));
                    Assert.True(server.OverlapsAnySettlement(edit.WorldPosition.X, edit.WorldPosition.Z), "Every actual column is reserved for subsequent content searches.");
                }
            }
            Assert.True(bounds.MaxX - bounds.MinX <= 80);
            Assert.True(bounds.MaxZ - bounds.MinZ <= 150); // includes the existing bounded north approach
            var site = server.VeylSurveyForTest()!.Value;
            Assert.True(server.World.GetBlock(site.Surface + new Vector3i(0, 0, -1)).IsAir);
            Assert.True(server.World.GetBlock(site.Surface + new Vector3i(0, 1, -1)).IsAir);
            var generated = VeylSurveyGenerator.Generate(_content, "stone", record.GeometryVersion);
            for (int z = 0; z <= 21; z++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var step = new Vector3i(record.X + generated.Width / 2 + dx, surface - z, record.Z + z);
                    Assert.Equal(ShapeCode.Pack(BlockShape.Stairs, 2), server.World.GetShape(step));
                    Assert.True(server.World.GetBlock(step + new Vector3i(0, 1, 0)).IsAir);
                    Assert.True(server.World.GetBlock(step + new Vector3i(0, 2, 0)).IsAir);
                }
        }
    }

    [Fact]
    public void Reload_KeepsLandscapeVersionExactReservationAndPlayerSculpture()
    {
        var first = Start("rocky", 4242, out var repoA);
        var record = Record(first);
        var edits = LandscapeEdits(first, repoA, record);
        Assert.NotEmpty(edits);
        var removed = edits[^1].WorldPosition;
        string bounds = JsonSerializer.Serialize(record.Reservation);
        List<(Vector3i Pos, ushort Block, int Tint, int Glow, int Shape)> snapshot;
        using (repoA)
        {
            first.World.SetBlock(removed, BlockId.Air, owner: "BasaltSculptor");
            snapshot = LandscapeEdits(first, repoA, record).Select(e => (e.WorldPosition, e.Block, e.Tint, e.Glow, e.Shape)).ToList();
        }
        var second = Start("rocky", 4242, out var repoB);
        using (repoB)
        {
            var replay = Record(second);
            Assert.Equal(1, replay.LandscapeVersion);
            Assert.Equal(bounds, JsonSerializer.Serialize(replay.Reservation));
            Assert.Equal(snapshot, LandscapeEdits(second, repoB, replay).Select(e => (e.WorldPosition, e.Block, e.Tint, e.Glow, e.Shape)).ToList());
            Assert.True(second.World.GetBlock(removed).IsAir);
            Assert.True(second.OverlapsAnySettlement(removed.X, removed.Z));
            Assert.Equal("BasaltSculptor", repoB.GetBlockAttribution(second.World.LocationId, removed)!.Value.Owner);
        }
    }

    [Fact]
    public void PinnedPreLandscapeRecord_NeverAddsColumnsOnReload()
    {
        var first = Start("rocky", 4242, out var repoA);
        var record = Record(first);
        var edits = LandscapeEdits(first, repoA, record);
        Assert.NotEmpty(edits);
        using (repoA)
        {
            // Arrange the pre-landscape persisted representation: original structure/terrain deltas,
            // a pinned version-zero record and no landscape columns. Reload must not migrate it.
            foreach (var column in edits.GroupBy(e => (e.WorldPosition.X, e.WorldPosition.Z)))
                repoA.DeleteBlockEdits(first.World.LocationId, column.First().WorldPosition, column.Last().WorldPosition);
            var metadata = repoA.LoadMetadata()!;
            var saved = Assert.Single(metadata.Placements.Where(p => p.Kind == "veyl_survey"));
            saved.LandscapeVersion = 0;
            repoA.SaveMetadata(metadata);
        }
        var second = Start("rocky", 4242, out var repoB);
        using (repoB)
        {
            var replay = Record(second);
            Assert.Equal(0, replay.LandscapeVersion);
            Assert.Empty(LandscapeEdits(second, repoB, replay));
            Assert.Equal((record.X, record.GroundY, record.Z, record.GeometryVersion), (replay.X, replay.GroundY, replay.Z, replay.GeometryVersion));
        }
    }

    private static object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    private static List<(int X, int Z, int Bottom, int Top)> Columns(object plan)
    {
        var terrain = plan.GetType().GetProperty("Terrain")!.GetValue(plan);
        if (terrain is null) return new();
        return ((IEnumerable)Property(terrain, "Columns")).Cast<object>()
            .Select(c => ((int)Property(c, "X"), (int)Property(c, "Z"), (int)Property(c, "SupportY"), (int)Property(c, "FloorY"))).ToList();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Plan_UsesCanonicalTorusSeedAndRejectsReservedOrEditedColumnEnvelopes(bool xSeam, bool zSeam)
    {
        var server = Start("rocky", 4242, out var repo, story: false);
        using (repo)
        {
            int circ = server.World.Circumference;
            int lat = WorldConstants.LatitudePeriodFor(circ);
            int x = xSeam ? circ - 5 : 200, z = zSeam ? lat / 2 - 5 : 200;
            var generator = (WorldGenerator)typeof(SvGameServer).GetField("_generator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
            var structure = VeylSurveyGenerator.Generate(_content, "stone");
            // The pure planning fixture sets a high enough marker datum to isolate support/reservation
            // invariants from the cosmetic landmark-height rejection. It never stamps or moves a player.
            int surface = generator.SurfaceHeight(server.World.Planet, x, z) + 32;
            var origin = new Vector3i(x, surface - VeylSurveyGenerator.BurialDepthFor(VeylSurveyGenerator.LatestVersion), z);
            var method = typeof(SvGameServer).GetMethod("PlanVeylLandscape", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object Plan(Vector3i at, List<(int, int, int, int)> reserved) => method.Invoke(server,
                new object?[] { structure, VeylSurveyGenerator.LatestVersion, at, null, reserved })!;
            var initial = Plan(origin, new());
            var columns = Columns(initial);
            Assert.NotEmpty(columns);
            Assert.InRange((int)Property(initial, "AddedCells"), 1, 20000);
            var shifted = Columns(Plan(origin + new Vector3i(circ, 0, lat), new()));
            Assert.Equal(columns, shifted.Select(c => (c.X - circ, c.Z - lat, c.Bottom, c.Top)).ToList());
            var column = columns[0];
            var edit = new Vector3i(column.X, column.Top, column.Z);
            server.World.SetBlock(edit, _content.GetBlock("glass")!.NumericId, owner: "TorusBuilder");
            Assert.DoesNotContain(Columns(Plan(origin, new())), c => c.X == column.X && c.Z == column.Z);
            Assert.Equal(_content.GetBlock("glass")!.NumericId, server.World.GetBlock(edit));
            Assert.Empty(Columns(Plan(origin, new() { (x, z, 120, 160) })));
            Assert.Equal("limited:content_reservation", Property(Plan(origin, new() { (x, z, 120, 160) }), "Outcome"));
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }
}
