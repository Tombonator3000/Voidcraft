// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The authored home is a new baseline, never an implicit migration of a player's existing ship.</summary>
public sealed class StarterHomeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_starter_home_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(string save, out SqliteWorldRepository repo, RecordingTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceWrecks = false,
            PlaceFactories = false,
            PlaceBanditCamps = false,
            PlaceChests = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceMonuments = false,
            ViewDistanceChunks = 1,
            Rules = new GameRules { StoryId = "none", FreeSpaceFlight = true },
        };
        var server = new SvGameServer(config, _content, transport ?? new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void ShipSnapshot_OmittedVersionIsLegacy_AndNewVersionSurvivesBothMappers()
    {
        var old = JsonSerializer.Deserialize<ShipSnapshot>("{\"ShipType\":\"starter\",\"Hull\":42}")!;
        Assert.Equal(0, StateMapper.FromSnapshot(old).StructureVersion);
        var ship = new ShipState { StructureVersion = 1, Hull = 73f };
        ship.Cargo.Add("iron_plate", 7, 99);
        var restored = StateMapper.FromSnapshot(JsonSerializer.Deserialize<ShipSnapshot>(
            JsonSerializer.Serialize(StateMapper.ToSnapshot(ship)))!);
        Assert.Equal(1, restored.StructureVersion);
        Assert.Equal(73f, restored.Hull);
        Assert.Equal(7, restored.Cargo.CountOf("iron_plate"));
    }

    [Fact]
    public void NewStarter_HasTwoMetreRouteFramedViewportReachableBaysAndFlushLighting()
    {
        var transport = new RecordingTransport();
        var server = Start("new", out var repo, transport);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.Equal(1, server.Ship.StructureVersion);
            var s = server.BuildShipStructureForTest("Pilot");
            Assert.Equal((6, 4, 11), (s.Width, s.Height, s.Length));
            Assert.Equal(7, s.StationCells.Count);
            var floor = _content.GetBlock("steel_floor")!.NumericId;
            for (int x = 2; x <= 3; x++)
                for (int z = 0; z <= 8; z++)
                {
                    Assert.Equal(floor, s.Get(new Vector3i(x, 0, z)));
                    for (int y = 1; y <= 3; y++) Assert.True(s.Get(new Vector3i(x, y, z)).IsAir);
                }
            for (int x = 1; x <= 4; x++)
                for (int y = 2; y <= 3; y++)
                    Assert.Equal(_content.GetBlock("glass")!.NumericId, s.Get(new Vector3i(x, y, 10)));
            foreach (var (_, cell) in s.StationCells)
            {
                float z = Math.Min(cell.Z + 0.5f, 8.5f);
                float dx = cell.X + 0.5f - 3f, dz = cell.Z + 0.5f - z;
                Assert.True(dx * dx + dz * dz <= 9f, "A station must be usable from the clear aisle.");
                Assert.True(s.Get(cell + new Vector3i(0, 2, 0)).IsAir, "No automatic lamp cube may hang over the bay.");
                Assert.Contains(s.Cells, c => c.Key.Y == 4 && c.Key.DistanceSquared(cell) <= 20
                    && _content.BlockById(c.Value)?.Key is "strip_light_warm" or "strip_light_cyan");
            }
            Assert.Equal(270, s.StationYaws[new Vector3i(1, 1, 4)]);
            Assert.Equal(90, s.StationYaws[new Vector3i(4, 1, 4)]);
            Assert.Equal(0, s.StationYaws[new Vector3i(2, 1, 9)]);
            Assert.Equal((int)BlockShape.Stairs, ShapeCode.ShapeOf(s.Shapes[new Vector3i(2, 0, -1)]));
            var doors = transport.Sent.Select(p => p.Message).OfType<DoorList>().Last();
            var hatch = Assert.Single(doors.Doors.Where(d => d.Kind == "energy"));
            Assert.Equal(2f, hatch.Width);
            var (origin, _) = server.LandedShipBoundsForTest("Pilot");
            Assert.Equal(new Vector3f(origin.X + 3.5f, origin.Y + 1f, origin.Z + 6.5f), server.HealTank);
            server.Stop();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PinnedVersion_PreservesOriginEditsAndRepairBaselineAcrossRestartAndSpaceVisit(int version)
    {
        string save = "version_" + version;
        using (var seed = new SqliteWorldRepository(new SaveGamePaths(_root, save)))
        {
            seed.Initialize();
            seed.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot" });
            seed.SaveShip("ship_Pilot", new ShipState
            {
                StructureVersion = version,
                Modules = _content.GetShip("starter")!.StartModules.ToList(),
            });
        }

        Vector3i origin, size;
        var removed = version == 0 ? new Vector3i(0, 2, 3) : new Vector3i(1, 3, 8);
        var built = new Vector3i(3, 1, 2);
        var exterior = new Vector3i(7, 1, 1);
        var placedBlock = _content.GetBlock("stone")!.NumericId;
        int originalTint = 0, originalShape = 0;
        var first = Start(save, out var repo1);
        using (repo1)
        {
            var player = first.AddLocalPlayer("Pilot");
            Assert.Equal(version, first.Ship.StructureVersion);
            var baseline = first.BuildShipStructureForTest("Pilot");
            if (version == 0)
            {
                Assert.Equal((5, 5, 7), (baseline.Width, baseline.Height, baseline.Length));
                Assert.Equal(151, baseline.Cells.Count);
                Assert.Equal(new[] { "medbay:1,1,4", "cockpit:2,1,5", "workshop:3,1,3", "cargo:1,1,3",
                    "quarters:3,1,4", "lab:1,1,5", "console:3,1,5" }, baseline.StationCells
                    .Select(s => $"{s.Type}:{s.Cell.X},{s.Cell.Y},{s.Cell.Z}"));
                Assert.Empty(baseline.StationYaws);
                Assert.Null(baseline.SpawnCell);
            }
            if (baseline.Mods.TryGetValue(removed, out var mod)) originalTint = mod.Tint;
            baseline.Shapes.TryGetValue(removed, out originalShape);
            (origin, size) = first.LandedShipBoundsForTest("Pilot");
            repo1.SetStructureBlock("ship:Pilot", removed, 0);
            repo1.SetStructureBlock("ship:Pilot", built, placedBlock.Value);
            repo1.SetStructureBlock("ship:Pilot", exterior, placedBlock.Value);
            Assert.True(first.SwitchShip(player.ActiveShipId));
            Assert.Equal(1, first.ShipRepairMissingCellsForTest("Pilot"));
            first.Stop();
        }

        var transport = new RecordingTransport();
        var reloaded = Start(save, out var repo2, transport);
        using (repo2)
        {
            var player = reloaded.AddLocalPlayer("Pilot");
            Assert.Equal(version, reloaded.Ship.StructureVersion);
            Assert.Equal((origin, size), reloaded.LandedShipBoundsForTest("Pilot"));
            var restored = reloaded.BuildShipStructureForTest("Pilot");
            Assert.True(restored.Get(removed).IsAir);
            Assert.Equal(placedBlock, restored.Get(built));
            Assert.Equal(placedBlock, restored.Get(exterior));
            player.State.InstantBuild = true;
            transport.Sent.Clear();
            reloaded.RepairShipForTest("Pilot", new RepairShipIntent { Mode = "cell", X = removed.X, Y = removed.Y, Z = removed.Z });
            var change = Assert.Single(transport.Sent.Select(s => s.Message).OfType<StructureBlockChanged>());
            Assert.Equal(originalTint, change.Tint);
            Assert.Equal(originalShape, change.Shape);
            Assert.Equal(0, reloaded.ShipRepairMissingCellsForTest("Pilot"));
            reloaded.EnterSpace("Pilot");
            Assert.True(reloaded.InSpace("Pilot"));
            reloaded.EnterShipInterior("Pilot");
            Assert.True(reloaded.InShipInterior("Pilot"));
            var inSpace = reloaded.BuildShipStructureForTest("Pilot");
            Assert.Equal(size.X, inSpace.Width);
            Assert.Equal(size.Z, inSpace.Length);
            Assert.Equal(placedBlock, inSpace.Get(built));
            Assert.Equal(placedBlock, inSpace.Get(exterior));
            Assert.False(inSpace.Get(removed).IsAir);
            reloaded.Stop();
        }
    }

    [Fact]
    public void AuthoredSpawn_ChoosesAdjacentClearFloorWhenPlayerBuiltOnThePreferredCell()
    {
        var server = Start("spawn", out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Pilot");
            repo.SetStructureBlock("ship:Pilot", new Vector3i(3, 1, 6), _content.GetBlock("stone")!.NumericId.Value);
            Assert.True(server.SwitchShip(player.ActiveShipId));
            var (origin, _) = server.LandedShipBoundsForTest("Pilot");
            Assert.Equal(new Vector3f(origin.X + 2.5f, origin.Y + 1f, origin.Z + 6.5f), server.HealTank);
            Assert.Equal(_content.GetBlock("stone")!.NumericId, server.BuildShipStructureForTest("Pilot").Get(new Vector3i(3, 1, 6)));
            server.Stop();
        }
    }

    [Fact]
    public void SideFacingWorkshop_ReplicatesMatchingStationAndOwnedSpecimenYaw()
    {
        var transport = new RecordingTransport();
        var server = Start("yaw", out var repo, transport);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            alice.State.Milestones.Add("survey:veyl:specimen:" + alice.ActiveShipId);
            server.BuildShipStructureForTest("Alice"); // point the served-player cursor at the owner
            transport.Sent.Clear();
            Assert.True(server.SwitchShip(alice.ActiveShipId));
            var stations = Assert.Single(transport.Sent.Where(p => p.Connection == alice.ConnectionId)
                .Select(p => p.Message).OfType<ShipStations>());
            Assert.Equal(270, Assert.Single(stations.Stations.Where(s => s.Type == "workshop")).Yaw);
            Assert.Equal(90, Assert.Single(stations.Stations.Where(s => s.Type == "quarters")).Yaw);
            var specimen = Assert.Single(transport.Sent.Where(p => p.Connection == bob.ConnectionId)
                .Select(p => p.Message).OfType<LandedShipState>());
            Assert.True(specimen.HasVeylSpecimen);
            Assert.Equal("Alice", specimen.PlayerId);
            Assert.Equal((1.5f, 2f, 4.5f, 270), (specimen.SpecimenX, specimen.SpecimenY, specimen.SpecimenZ, specimen.SpecimenYaw));
            var wire = Assert.IsType<LandedShipState>(NetCodec.Decode(NetCodec.EncodeJson(specimen)));
            Assert.Equal(270, wire.SpecimenYaw);
            Assert.DoesNotContain("survey:veyl:specimen:" + bob.ActiveShipId, bob.State.Milestones);
            server.Stop();
        }
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly List<(int Connection, object Message)> Sent = new();
        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } message) Sent.Add((connectionId, message));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } message) Sent.Add((int.MinValue, message));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
