// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
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

public sealed class VeylSurveyPlacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_survey_seating_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(string planet, long seed, out SqliteWorldRepository repo, bool story = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "seating"));
        var config = new ServerConfig
        {
            WorldName = "seating",
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { StoryId = story ? "voidcraft_awakening" : "none" },
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static StructurePlacementRecord Record(SvGameServer server)
        => Assert.Single(server.PlacementRecordsForTest.Where(p => p.Kind == "veyl_survey" && p.Placed));

    [Theory]
    [InlineData("tablelands", 37)]
    [InlineData("ocean", 71)]
    [InlineData("highland", 23)]
    [InlineData("rocky", 11)]
    [InlineData("swamp", 67)]
    public void PreviouslyDroppedSurvey_GetsDryOpenStairAndGroundedTerrace(string planet, long seed)
    {
        var server = Start(planet, seed, out var repo);
        using (repo)
        {
            var record = Record(server);
            Assert.Contains(record.Seat, new[] { "terrace_v1", "wellhead_v1" });
            Assert.Equal(VeylSurveyGenerator.LatestVersion, record.GeometryVersion);
            var bounds = Assert.IsType<StructureReservationBounds>(record.Reservation);
            var min = new Vector3i(bounds.MinX!.Value, bounds.MinY!.Value, bounds.MinZ!.Value);
            var max = new Vector3i(bounds.MaxX!.Value, bounds.MaxY!.Value, bounds.MaxZ!.Value);
            bool Reserved(float x, float z, int halfX = 0, int halfZ = 0)
                => Math.Abs(WorldConstants.WrapDeltaX((int)x - (min.X + max.X) / 2, server.World.Circumference))
                    < (max.X - min.X + 2) / 2 + halfX
                && Math.Abs(WorldConstants.WrapDeltaZ((int)z - (min.Z + max.Z) / 2, server.World.Circumference))
                    < (max.Z - min.Z + 2) / 2 + halfZ;
            foreach (var factory in server.FactoriesForTest)
                Assert.False(Reserved((factory.Min.X + factory.Max.X) / 2f, (factory.Min.Z + factory.Max.Z) / 2f,
                    (factory.Max.X - factory.Min.X + 2) / 2, (factory.Max.Z - factory.Min.Z + 2) / 2));
            foreach (var vault in server.VaultEntrances) Assert.False(Reserved(vault.X, vault.Z, 4, 4));
            foreach (var cube in server.DataCubeSnapshots) Assert.False(Reserved(cube.Pos.X, cube.Pos.Z));
            foreach (var container in server.Containers) Assert.False(Reserved(container.Position.X, container.Position.Z));
            Assert.Equal(("veyl_survey", 1, 1), Assert.Single(server.StampReportForTest.Where(r => r.Kind == "veyl_survey")));
            var structure = VeylSurveyGenerator.Generate(_content, "stone", record.GeometryVersion);
            var origin = new Vector3i(record.X, record.GroundY, record.Z);
            int burial = VeylSurveyGenerator.BurialDepthFor(record.GeometryVersion);
            var site = server.VeylSurveyForTest()!.Value;
            Assert.Equal(_content.GetBlock("rune_stone")!.NumericId, server.World.GetBlock(site.Surface));
            for (int dy = 0; dy <= 1; dy++)
                Assert.True(server.World.GetBlock(site.Surface + new Vector3i(0, dy, -1)).IsAir);
            for (int z = 0; z <= 21; z++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var step = origin + new Vector3i(structure.Width / 2 + dx, burial - z, z);
                    int expectedShape = Math.Abs(dx) <= 1
                        ? ShapeCode.Pack(BlockShape.Ramp, 2)
                        : ShapeCode.Pack(BlockShape.Stairs, 2);
                    Assert.Equal(expectedShape, server.World.GetShape(step));
                    Assert.True(server.World.GetBlock(step + new Vector3i(0, 1, 0)).IsAir);
                    Assert.True(server.World.GetBlock(step + new Vector3i(0, 2, 0)).IsAir);
                }

            var generator = (WorldGenerator)typeof(SvGameServer).GetField("_generator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server)!;
            int approach = record.Z - min.Z - 2;
            int end = int.MinValue;
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = record.X + structure.Width / 2 + dx, z = record.Z - approach;
                int top = generator.SurfaceHeight(server.World.Planet, x, z);
                if (generator.TryGetWaterSurface(server.World.Planet, x, z, out int water, out _)) top = Math.Max(top, water);
                end = Math.Max(end, top);
            }
            int surface = record.GroundY + burial;
            for (int distance = 1; distance <= approach; distance++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int y = surface + (end - surface) * distance / approach;
                    var row = new Vector3i(record.X + structure.Width / 2 + dx, y, record.Z - distance);
                    Assert.True(server.World.Definition(server.World.GetBlock(row))?.Solid == true);
                    Assert.True(server.World.Definition(server.World.GetBlock(row + new Vector3i(0, -1, 0)))?.Solid == true);
                    Assert.True(server.World.GetBlock(row + new Vector3i(0, 1, 0)).IsAir);
                    Assert.True(server.World.GetBlock(row + new Vector3i(0, 2, 0)).IsAir);
                    int shape = server.World.GetShape(row);
                    Assert.Contains(shape, new[] { 0, ShapeCode.Pack(BlockShape.Stairs, 0), ShapeCode.Pack(BlockShape.Stairs, 2) });
                    if (shape != 0) Assert.Equal(ShapeCode.Pack(BlockShape.Stairs, end < surface ? 0 : 2), shape);
                }
            // The west apron is outside the authored chamber. Every support cell down to generated
            // ground/seabed is solid, so the fallback cannot hide a suspended platform beneath water.
            int floor = record.GroundY + burial - 3;
            for (int dz = 0; dz < structure.Length; dz += 3)
            {
                int x = record.X - 3, z = record.Z + dz;
                int natural = generator.SurfaceHeight(server.World.Planet, x, z);
                if (generator.TryGetWaterSurface(server.World.Planet, x, z, out int water, out int seabed))
                {
                    natural = seabed;
                    Assert.True(record.GroundY + burial > water);
                }
                Assert.InRange(floor - natural, -96, 96);
                for (int y = Math.Min(natural, floor); y <= floor; y++)
                    Assert.True(server.World.Definition(server.World.GetBlock(new Vector3i(x, y, z)))?.Solid == true,
                        $"Unsupported terrace column at {x},{y},{z} on {planet}/{seed}.");
                Assert.True(server.World.GetBlock(new Vector3i(x, floor + 1, z)).IsAir);
                Assert.True(server.World.GetBlock(new Vector3i(x, floor + 2, z)).IsAir);
            }
        }
    }

    [Fact]
    public void OceanTerrace_ReloadPreservesPinnedSeatAndPlayerExcavation()
    {
        var first = Start("ocean", 71, out var repoA);
        var record = Record(first);
        var position = new Vector3i(record.X - 3,
            record.GroundY + VeylSurveyGenerator.BurialDepthFor(record.GeometryVersion) - 3, record.Z + 8);
        var contact = first.VeylSurveyForTest()!.Value.Contact;
        using (repoA)
        {
            first.World.SetBlock(position, BlockId.Air, owner: "TerraceBuilder");
            first.World.SetBlock(contact + new Vector3i(0, 0, -1), BlockId.Air, owner: "TerraceBuilder");
        }
        var second = Start("ocean", 71, out var repoB);
        using (repoB)
        {
            var replay = Record(second);
            Assert.Equal((record.X, record.GroundY, record.Z, record.GeometryVersion, record.Seat),
                (replay.X, replay.GroundY, replay.Z, replay.GeometryVersion, replay.Seat));
            var expected = Assert.IsType<StructureReservationBounds>(record.Reservation);
            var actual = Assert.IsType<StructureReservationBounds>(replay.Reservation);
            Assert.Equal((expected.MinX, expected.MinY, expected.MinZ, expected.MaxX, expected.MaxY, expected.MaxZ),
                (actual.MinX, actual.MinY, actual.MinZ, actual.MaxX, actual.MaxY, actual.MaxZ));
            Assert.True(actual.MinX < record.X && actual.MinZ < record.Z);
            Assert.True(actual.MaxX > record.X + 24 && actual.MaxZ > record.Z + 56);
            Assert.True(second.OverlapsAnySettlement(position.X, position.Z));
            Assert.True(second.World.GetBlock(position).IsAir);
            Assert.True(second.World.GetBlock(contact + new Vector3i(0, 0, -1)).IsAir);
            Assert.Equal(contact, second.VeylSurveyForTest()!.Value.Contact);
        }
    }

    [Fact]
    public void AlreadyEditedOceanWorld_DoesNotRunFreshWorldTerraformFallback()
    {
        var first = Start("ocean", 71, out var repoA, story: false);
        var edit = new Vector3i(225, 100, 225);
        using (repoA)
        {
            first.World.SetBlock(edit, _content.GetBlock("glass")!.NumericId, owner: "ExistingBuilder");
            var metadata = repoA.LoadMetadata()!;
            metadata.RulesOverride!.StoryId = "voidcraft_awakening";
            repoA.SaveMetadata(metadata);
        }
        var second = Start("ocean", 71, out var repoB);
        using (repoB)
        {
            Assert.Null(second.VeylSurveyForTest());
            Assert.DoesNotContain(second.PlacementRecordsForTest, p => p.Kind == "veyl_survey");
            Assert.Equal(_content.GetBlock("glass")!.NumericId, second.World.GetBlock(edit));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TerrainEditEnvelope_ProtectsPlayerCellsAcrossBothTorusSeams(bool xSeam, bool zSeam)
    {
        var server = Start("rocky", 11, out var repo, story: false);
        using (repo)
        {
            int circ = server.World.Circumference;
            int x = xSeam ? circ - 2 : 40;
            int z = zSeam ? WorldConstants.LatitudePeriodFor(circ) / 2 - 2 : 40;
            var min = new Vector3i(x - 2, 80, z - 2);
            var max = new Vector3i(x + 6, 100, z + 6);
            var check = typeof(SvGameServer).GetMethod("VeylTerrainHasPlayerEdits", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.False((bool)check.Invoke(server, new object[] { min, max })!);
            server.World.SetBlock(new Vector3i(x + 4, 92, z + 4), BlockId.Air, owner: "WrappedBuilder");
            Assert.True((bool)check.Invoke(server, new object[] { min, max })!);
            Assert.False((bool)check.Invoke(server, new object[] { min + new Vector3i(0, 30, 0), max + new Vector3i(0, 30, 0) })!);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void PinnedTemplateAndExtendedApproach_AreReservedBeforeMonumentRegistration(int version)
    {
        var server = Start("rocky", 11, out var repo, story: false);
        using (repo)
        {
            int circ = server.World.Circumference;
            int z = WorldConstants.LatitudePeriodFor(circ) / 2 - 4;
            var origin = new Vector3i(circ - 8, 60, z);
            var record = new StructurePlacementRecord
            {
                LocationId = server.World.LocationId,
                Kind = "veyl_survey",
                Placed = true,
                GeometryVersion = version,
                X = origin.X,
                GroundY = origin.Y,
                Z = origin.Z,
            };
            if (version >= 4)
            {
                record.Reservation = new StructureReservationBounds
                {
                    MinX = origin.X - 5,
                    MinY = origin.Y - 5,
                    MinZ = origin.Z - 35,
                    MaxX = origin.X + 30,
                    MaxY = origin.Y + 50,
                    MaxZ = origin.Z + 62,
                };
            }
            server.Metadata.Placements.Add(record);
            // No survey runtime instance exists: the collision queries must use pinned metadata.
            Assert.Null(server.VeylSurveyForTest());
            int queryX = WorldConstants.WrapX(origin.X + 10, circ);
            int queryZ = WorldConstants.WrapZ(origin.Z + (version == 0 ? 10 : -30), circ);
            Assert.True(server.OverlapsAnySettlement(queryX, queryZ));
            var search = typeof(SvGameServer).GetMethod("OverlapsFootprint", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)search.Invoke(server, new object[]
            {
                queryX, queryZ, 1, 1, new List<(int, int, int, int)>(), 2,
            })!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialOrReversedReservation_FallsBackToSavedTemplate(bool reversed)
    {
        var server = Start("rocky", 11, out var repo, story: false);
        using (repo)
        {
            var bounds = reversed
                ? new StructureReservationBounds { MinX = 10, MinY = 10, MinZ = 10, MaxX = -10, MaxY = -10, MaxZ = -10 }
                : new StructureReservationBounds { MinX = 0 };
            server.Metadata.Placements.Add(new StructurePlacementRecord
            {
                LocationId = server.World.LocationId,
                Kind = "veyl_survey",
                Placed = true,
                X = 350,
                GroundY = 50,
                Z = 150,
                GeometryVersion = 0,
                Reservation = bounds,
            });
            var check = typeof(SvGameServer).GetMethod("OverlapsPinnedVeyl", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)check.Invoke(server, new object[] { 355, 155, 1, 1, 2 })!);
            Assert.False((bool)check.Invoke(server, new object[] { 0, 0, 1, 1, 2 })!);
        }
    }

    [Theory]
    [InlineData(-15)]
    [InlineData(0)]
    [InlineData(15)]
    public void ApproachProfile_HasOnlyHalfMetreRisesWithCorrectStairOrientation(int heightChange)
    {
        const int Surface = 100, Length = 20;
        var shapeFor = typeof(SvGameServer).GetMethod("VeylApproachShape", BindingFlags.Static | BindingFlags.NonPublic)!;
        float previousNorthEdge = Surface + 1;
        int stairRows = 0;
        for (int distance = 1; distance <= Length; distance++)
        {
            int floor = Surface + heightChange * distance / Length;
            int shape = (int)shapeFor.Invoke(null, new object[] { Surface, Surface + heightChange, distance, Length })!;
            float southEdge = floor + (shape == ShapeCode.Pack(BlockShape.Stairs, 2) ? 0.5f : 1f);
            float northEdge = floor + (shape == ShapeCode.Pack(BlockShape.Stairs, 0) ? 0.5f : 1f);
            Assert.InRange(Math.Abs(southEdge - previousNorthEdge), 0, 0.5f);
            Assert.InRange(Math.Abs(northEdge - southEdge), 0, 0.5f);
            if (shape != 0)
            {
                stairRows++;
                Assert.Equal(ShapeCode.Pack(BlockShape.Stairs, heightChange < 0 ? 0 : 2), shape);
            }
            previousNorthEdge = northEdge;
        }
        Assert.Equal(Surface + heightChange + 1, previousNorthEdge);
        Assert.Equal(heightChange == 0, stairRows == 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
