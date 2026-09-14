// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

public sealed class VeylSurveyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_survey_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(out SqliteWorldRepository repo, out LoopbackClientTransport client, bool story = true,
        bool ships = true, IServerTransport? transport = null, Action<ServerConfig>? configure = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "survey"));
        var link = new LoopbackLink();
        client = new LoopbackClientTransport(link);
        var config = new ServerConfig
        {
            WorldName = "survey",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = ships,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceWrecks = false,
            PlaceFactories = false,
            PlaceBanditCamps = false,
            PlaceChests = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            ViewDistanceChunks = 1,
            Rules = new GameRules { StoryId = story ? "voidcraft_awakening" : "none" },
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(link), repo);
        server.Start();
        return server;
    }

    private static string Stage(SvGameServer server, string player)
        => Assert.Single(server.PlanetPoisForTest(player).Where(p => p.Type.StartsWith("veyl_", StringComparison.Ordinal))).Type;

    private static void StandAt(PlayerSession player, Vector3i pos)
    {
        player.State.AboardShip = false;
        player.State.Position = new Vector3f(pos.X + 0.5f, pos.Y, pos.Z - 2.5f);
    }

    private static void StandInShip(SvGameServer server, PlayerSession player, string? owner = null)
    {
        var (origin, size) = server.LandedShipBoundsForTest(owner ?? player.State.PlayerId);
        Assert.True(size.X > 0);
        player.State.Position = new Vector3f(origin.X + size.X / 2f, origin.Y + 1f, origin.Z + size.Z / 2f);
        player.State.AboardShip = true;
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

    private static void Mine(SvGameServer server, PlayerSession player, Vector3i block)
    {
        server.MineBlock(player.State.PlayerId, block.X, block.Y, block.Z);
        Assert.True(server.World.GetBlock(block).IsAir, $"The ordinary mining intent must excavate {block}.");
    }

    private static void Excavate(SvGameServer server, PlayerSession player, Vector3i core, Vector3i socket)
    {
        // The open stair ends three blocks before the contact. Drill a walkable tunnel from that landing.
        StandAt(player, core);
        for (int dz = -2; dz <= -1; dz++)
            for (int dy = 0; dy <= 1; dy++)
                Mine(server, player, core + new Vector3i(0, dy, dz));
        Mine(server, player, socket);
    }

    [Fact]
    public void Structure_HasOpenDescent_BuriedContactAndStableBasaltSilhouette()
    {
        var a = VeylSurveyGenerator.Generate(_content, "stone", 1);
        var b = VeylSurveyGenerator.Generate(_content, "stone", 0);
        for (int z = 0; z <= 6; z++)
        {
            int floor = VeylSurveyGenerator.BurialDepth - z;
            for (int x = 7; x <= 9; x++)
            {
                Assert.NotEqual(0, a.Get(x, floor, z));
                Assert.Equal(0, a.Get(x, floor + 1, z));
                Assert.Equal(0, a.Get(x, floor + 2, z));
            }
        }
        var core = VeylSurveyGenerator.BuriedContact;
        Assert.Equal(_content.GetBlock("rune_stone")!.NumericId.Value, a.Get(core.X, core.Y, core.Z));
        Assert.NotEqual(0, a.Get(core.X, core.Y, core.Z - 1));
        Assert.Equal(_content.GetBlock("basalt")!.NumericId.Value, a.Get(11, 14, 11));
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                    Assert.Equal(a.GetModifier(x, y, z), b.GetModifier(x, y, z));
                    Assert.Equal(a.GetShape(x, y, z), b.GetShape(x, y, z));
                }
    }

    [Fact]
    public void Vault_HasBuriedVolume_ContinuousBridge_ReachableSealAndHalfMeterReturnSteps()
    {
        var vault = VeylSurveyGenerator.Generate(_content, "stone", geometryVersion: 2);
        Assert.Equal((25, 45, 57), (vault.Width, vault.Height, vault.Length));
        Assert.Equal(26, VeylSurveyGenerator.BurialDepthFor(2));
        Assert.Equal(VeylSurveyGenerator.VaultSurfaceContact,
            Assert.Single(vault.Markers.Where(m => m.Type == "survey_surface")).LocalPos);
        Assert.Equal(VeylSurveyGenerator.VaultBuriedContact,
            Assert.Single(vault.Markers.Where(m => m.Type == "survey_contact")).LocalPos);
        // The natural surface is local y26; the roof belongs below/at that surface, not 19 m above it.
        Assert.NotEqual(0, vault.Get(12, 26, 40));
        Assert.Equal(0, vault.Get(12, 27, 40));
        Assert.Equal(0, VeylSurveyGenerator.MinimumCarveHeight(2, 12, 40));
        Assert.Equal(8, VeylSurveyGenerator.MinimumCarveHeight(2, 4, 27));
        for (int z = 0; z <= 21; z++)
            for (int x = 11; x <= 13; x++)
            {
                int y = 26 - z;
                Assert.Equal(ShapeCode.Pack(BlockShape.Stairs, 2), vault.GetShape(x, y, z));
                for (int head = 1; head <= 3; head++) Assert.Equal(0, vault.Get(x, y + head, z));
            }
        for (int z = 22; z < 46; z++)
            for (int x = 11; x <= 12; x++)
            {
                Assert.NotEqual(0, vault.Get(x, 4, z));
                Assert.Equal(0, vault.Get(x, 5, z));
                Assert.Equal(0, vault.Get(x, 6, z));
                Assert.Equal(0, vault.GetShape(x, 4, z));
            }
        Assert.Equal(0, vault.Get(13, 4, 33)); // A visible broken edge, alongside the intact route.
        for (int z = 28; z <= 31; z++)
            for (int x = 3; x <= 4; x++)
                Assert.Equal(ShapeCode.Pack(BlockShape.Stairs, 2), vault.GetShape(x, 32 - z, z));
        var core = VeylSurveyGenerator.VaultBuriedContact;
        Assert.NotEqual(0, vault.Get(core.X, core.Y, core.Z - 1));
        Assert.NotEqual(0, vault.Get(core.X, core.Y + 1, core.Z - 1));
        Assert.Equal(_content.GetBlock("rune_stone")!.NumericId.Value, vault.Get(core.X, core.Y, core.Z));
        Assert.Equal(0, vault.GetShape(13, 4, 47));
        Assert.NotEqual((0, 0), vault.GetModifier(13, 4, 47));
        Assert.NotEqual(0, vault.Get(12, 16, 50)); // Suspended anchor, with empty space underneath.
        Assert.Equal(0, vault.Get(12, 9, 50));
    }

    [Fact]
    public void CurrentVault_HasRepairableShortcutAndClearTwoMeterSideRoute()
    {
        var vault = VeylSurveyGenerator.Generate(_content, "stone");
        Assert.Equal(26, VeylSurveyGenerator.BurialDepthFor(VeylSurveyGenerator.LatestVersion));
        for (int x = 11; x <= 13; x++)
            for (int z = 35; z <= 37; z++)
            {
                Assert.Equal(0, vault.Get(x, 4, z));
                Assert.NotEqual(0, vault.Get(x, 0, z)); // Recoverable floor four meters below the route.
            }
        // Both entry and seal connect to a continuous gallery with two clear meters of headroom.
        var route = new HashSet<(int X, int Z)>();
        for (int z = 28; z <= 45; z++)
            for (int x = 7; x <= 8; x++) route.Add((x, z));
        foreach (int z in new[] { 28, 29, 44, 45 })
            for (int x = 7; x <= 13; x++) route.Add((x, z));
        foreach (var (x, z) in route)
        {
            Assert.NotEqual(0, vault.Get(x, 4, z));
            Assert.Equal(0, vault.GetShape(x, 4, z));
            Assert.Equal(0, vault.Get(x, 5, z));
            Assert.Equal(0, vault.Get(x, 6, z));
        }
        // Version two remains the exact continuous central route used by earlier saved placements.
        var previous = VeylSurveyGenerator.Generate(_content, "stone", geometryVersion: 2);
        Assert.NotEqual(0, previous.Get(12, 4, 36));
        Assert.Equal(0, previous.Get(7, 4, 36));
    }

    [Fact]
    public void CurrentVault_LightsTheGapAndAnchor_WithoutChangingVersionThreeCollisionOrMarkers()
    {
        var previous = VeylSurveyGenerator.Generate(_content, "stone", geometryVersion: 3);
        var current = VeylSurveyGenerator.Generate(_content, "stone");
        for (int x = 0; x < previous.Width; x++)
            for (int y = 0; y < previous.Height; y++)
                for (int z = 0; z < previous.Length; z++)
                {
                    Assert.Equal(previous.Get(x, y, z) == 0, current.Get(x, y, z) == 0);
                    Assert.Equal(previous.GetShape(x, y, z), current.GetShape(x, y, z));
                }
        Assert.Equal(previous.Markers.Select(m => (m.Type, m.LocalPos)),
            current.Markers.Select(m => (m.Type, m.LocalPos)));
        Assert.Equal((0x577B87, 0x237A8C), previous.GetModifier(12, 16, 48));
        Assert.Equal((0x577B87, 0x70C6DA), current.GetModifier(12, 16, 48));
        Assert.Equal(previous.GetModifier(19, 32, 9), current.GetModifier(19, 32, 9));
        var warm = _content.GetBlock("strip_light_warm")!.NumericId.Value;
        Assert.Equal(warm, current.Get(11, 4, 34));
        Assert.Equal(warm, current.Get(11, 4, 38));
        Assert.NotEqual(warm, previous.Get(11, 4, 34));
        Assert.Equal(0, current.Get(11, 4, 35)); // Light never fills the construction challenge.
    }

    [Fact]
    public void BridgeShortcut_UsesOrdinaryReachAndMaterials_AndPersistsAcrossReload()
    {
        var server = Start(out var repo, out var client);
        Vector3i first;
        using (repo)
        using (client)
        {
            var record = Assert.Single(server.PlacementRecordsForTest.Where(r => r.Kind == "veyl_survey"));
            first = new Vector3i(record.X + 12, record.GroundY + 4, record.Z + 35);
            var player = server.AddLocalPlayer("Builder");
            player.State.AboardShip = false;
            player.State.Inventory.Add("basalt", 3, 1024);
            int before = player.State.Inventory.CountOf("basalt");
            server.PlaceBlock("Builder", first.X, first.Y, first.Z, "basalt");
            Assert.True(server.World.GetBlock(first).IsAir); // Still near the distant landing pad.
            Assert.Equal(before, player.State.Inventory.CountOf("basalt"));
            player.State.AboardShip = false;
            player.State.Position = new Vector3f(first.X + 0.5f, first.Y + 1f, first.Z - 0.75f);
            for (int i = 0; i < 3; i++)
            {
                server.PlaceBlock("Builder", first.X, first.Y, first.Z + i, "basalt");
                Assert.Equal(_content.GetBlock("basalt")!.NumericId,
                    server.World.GetBlock(first + new Vector3i(0, 0, i)));
            }
            Assert.Equal(before - 3, player.State.Inventory.CountOf("basalt"));
            Assert.Equal("veyl_signal", Stage(server, "Builder")); // Building alone does not complete the survey.
            repo.SavePlayer(player.State);
        }
        var reloaded = Start(out var repo2, out var client2);
        using (repo2)
        using (client2)
        {
            for (int i = 0; i < 3; i++)
                Assert.Equal(_content.GetBlock("basalt")!.NumericId,
                    reloaded.World.GetBlock(first + new Vector3i(0, 0, i)));
            Assert.Equal(VeylSurveyGenerator.LatestVersion,
                Assert.Single(reloaded.PlacementRecordsForTest.Where(r => r.Kind == "veyl_survey")).GeometryVersion);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void PinnedSite_RetainsMarkersExcavationAndRepairAcrossVersionedLoad(int version, bool wrap)
    {
        var initial = Start(out var repo, out var client, story: false, ships: false);
        Vector3i origin;
        Vector3i excavated;
        Vector3i socket;
        using (repo)
        using (client)
        {
            Assert.Null(initial.VeylSurveyForTest());
            var legacy = VeylSurveyGenerator.Generate(_content, "stone", version);
            origin = new Vector3i(wrap ? initial.World.Circumference - 8 : 300, 40, 300);
            repo.RunInTransaction(() =>
            {
                for (int x = 0; x < legacy.Width; x++)
                    for (int y = 0; y < legacy.Height; y++)
                        for (int z = 0; z < legacy.Length; z++)
                        {
                            var (tint, glow) = legacy.GetModifier(x, y, z);
                            initial.World.SetBlock(origin + new Vector3i(x, y, z),
                                new BlocksBeyondTheStars.Shared.Primitives.BlockId(legacy.Get(x, y, z)),
                                tint, glow, legacy.GetShape(x, y, z));
                        }
            });
            excavated = origin + Assert.Single(legacy.Markers.Where(m => m.Type == "survey_contact")).LocalPos
                + new Vector3i(0, 0, -1);
            socket = origin + Assert.Single(legacy.Markers.Where(m => m.Type == "survey_socket")).LocalPos;
            initial.World.SetBlock(excavated, BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            initial.World.SetBlock(socket, _content.GetBlock("stone")!.NumericId, 0, 0,
                ShapeCode.Pack(BlockShape.Ramp, 1));
            var meta = repo.LoadMetadata()!;
            meta.RulesOverride!.StoryId = "voidcraft_awakening";
            meta.Placements.Add(new StructurePlacementRecord
            {
                Kind = "veyl_survey",
                Index = 0,
                LocationId = initial.World.LocationId,
                Placed = true,
                X = origin.X,
                GroundY = origin.Y,
                Z = origin.Z,
                Name = "veyl_anchor",
                Seat = "buried",
                GeometryVersion = version,
            });
            repo.SaveMetadata(meta);
        }
        var loaded = Start(out var repo2, out var client2, ships: false);
        using (repo2)
        using (client2)
        {
            var site = loaded.VeylSurveyForTest()!.Value;
            var pinned = VeylSurveyGenerator.Generate(_content, "stone", version);
            Assert.Equal(origin + Assert.Single(pinned.Markers.Where(m => m.Type == "survey_surface")).LocalPos, site.Surface);
            Assert.Equal(origin + Assert.Single(pinned.Markers.Where(m => m.Type == "survey_contact")).LocalPos, site.Contact);
            Assert.Equal(socket, site.Socket);
            Assert.True(loaded.World.GetBlock(excavated).IsAir);
            Assert.Equal(ShapeCode.Pack(BlockShape.Ramp, 1), loaded.World.GetShape(socket));
            Assert.Equal(version, Assert.Single(loaded.PlacementRecordsForTest.Where(r => r.Kind == "veyl_survey")).GeometryVersion);
        }
    }

    [Fact]
    public void MissingGeometryVersion_UsesLegacy_AndUnsupportedVersionCannotRebindContacts()
    {
        var record = System.Text.Json.JsonSerializer.Deserialize<StructurePlacementRecord>("{}")!;
        Assert.Equal(0, record.GeometryVersion);
        Assert.Equal(6, VeylSurveyGenerator.BurialDepthFor(record.GeometryVersion));
        Assert.Throws<ArgumentOutOfRangeException>(() => VeylSurveyGenerator.Generate(_content, "stone", 99));
    }

    [Fact]
    public void Expedition_RequiresExposedScanAndSupportedShape_ThenAwardsUsableScannerOnceAcrossReload()
    {
        var server = Start(out var repo, out var client);
        (Vector3i Surface, Vector3i Contact, Vector3i Socket) site;
        using (repo)
        using (client)
        {
            site = Assert.IsType<(Vector3i Surface, Vector3i Contact, Vector3i Socket)>(server.VeylSurveyForTest());
            Assert.Equal(VeylSurveyGenerator.LatestVersion,
                Assert.Single(server.PlacementRecordsForTest.Where(r => r.Kind == "veyl_survey")).GeometryVersion);
            var player = server.AddLocalPlayer("Surveyor");
            Assert.Equal("veyl_signal", Stage(server, "Surveyor"));
            StandAt(player, site.Surface);
            Assert.True(server.ScanSubject("Surveyor", "block", "rune_stone").FirstTime);
            Assert.Equal("veyl_excavate", Stage(server, "Surveyor"));
            // Standing close to a buried contact must not skip excavation, even with the right subject key.
            StandAt(player, site.Contact);
            server.ScanSubject("Surveyor", "block", "rune_stone");
            Assert.Equal("veyl_excavate", Stage(server, "Surveyor"));
            Excavate(server, player, site.Contact, site.Socket);
            server.ScanSubject("Surveyor", "block", "rune_stone");
            Assert.Equal("veyl_shape", Stage(server, "Surveyor"));
            var target = Assert.Single(server.PlanetPoisForTest("Surveyor").Where(p => p.Type == "veyl_shape"));
            Assert.Equal(site.Socket.X + 0.5f, target.X);
            Assert.Equal(site.Socket.Z + 0.5f, target.Z);
            // Normal cubes do not answer. Crafting a form away from the socket does not answer either.
            player.State.Inventory.Add("stone", 3, 1024);
            server.PlaceBlock("Surveyor", site.Socket.X, site.Socket.Y, site.Socket.Z, "stone");
            Assert.Equal("veyl_shape", Stage(server, "Surveyor"));
            Mine(server, player, site.Socket);
            server.ShapeCraft("Surveyor", "stone", (int)BlockShape.Ramp);
            Assert.Equal("veyl_shape", Stage(server, "Surveyor"));
            var messages = new List<object>();
            client.PayloadReceived += payload => { if (NetCodec.Decode(payload) is { } message) messages.Add(message); };
            string ramp = ItemKey.Compose("stone", 0, 0, (int)BlockShape.Ramp);
            var support = site.Socket + new Vector3i(0, -1, 0);
            Mine(server, player, support);
            server.PlaceBlock("Surveyor", site.Socket.X, site.Socket.Y, site.Socket.Z, ramp, upFace: 2, yaw: 0);
            Assert.Equal("veyl_shape", Stage(server, "Surveyor"));
            Mine(server, player, site.Socket);
            // Mining preserves the amber base's tint/glow in the inventory key. Re-place that actual item.
            string recoveredBase = player.State.Inventory.Slots.First(s => s is not null && ItemKey.Base(s.Item) == "basalt")!.Item;
            server.PlaceBlock("Surveyor", support.X, support.Y, support.Z, recoveredBase);
            Assert.False(server.World.GetBlock(support).IsAir);
            server.PlaceBlock("Surveyor", site.Socket.X, site.Socket.Y, site.Socket.Z, ramp, upFace: 2, yaw: 0);
            Assert.Equal("veyl_return", Stage(server, "Surveyor"));
            Assert.False(player.State.Inventory.Has("terrain_scanner", 1));
            server.VeylSurveyHomecomingForTest("Surveyor");
            Assert.False(player.State.Inventory.Has("terrain_scanner", 1));
            StandInShip(server, player);
            server.VeylSurveyHomecomingForTest("Surveyor");
            client.Poll();
            Assert.Equal(1, player.State.Inventory.CountOf("terrain_scanner"));
            Assert.Contains("terrain_scanner", player.State.UnlockedBlueprints);
            Assert.Contains(messages.OfType<PlayerStateUpdate>(), m => m.VeylSurveyComplete);
            Assert.Contains(messages.OfType<InventoryUpdate>(), m => m.UnlockedBlueprints.Contains("terrain_scanner"));
            Assert.Contains(messages.OfType<BlockChanged>(), m => m.X == site.Contact.X && m.Y == site.Contact.Y && m.Glow == 0x66ECFF);
            // The reward is the actual server-validated gadget action, including cost and a real ore readout.
            StandAt(player, site.Contact);
            var ore = site.Contact + new Vector3i(3, -2, 0);
            server.World.SetBlock(ore, _content.GetBlock("iron_ore")!.NumericId);
            float energy = player.State.SuitEnergy;
            server.UseGadgetForTest("Surveyor", "terrain_scanner", player.State.Position);
            client.Poll();
            Assert.True(player.State.SuitEnergy < energy);
            Assert.Contains(messages.OfType<OreScanResult>(), m => Enumerable.Range(0, m.X.Length).Any(i => m.X[i] == ore.X && m.Y[i] == ore.Y && m.Z[i] == ore.Z));
            server.VeylSurveyHomecomingForTest("Surveyor");
            Assert.Equal(1, player.State.Inventory.CountOf("terrain_scanner"));
            repo.SavePlayer(player.State);
        }
        var reloaded = Start(out var repo2, out var client2);
        using (repo2)
        using (client2)
        {
            Assert.Equal(site, reloaded.VeylSurveyForTest());
            var player = reloaded.AddLocalPlayer("Surveyor");
            player.State.AboardShip = true;
            reloaded.VeylSurveyHomecomingForTest("Surveyor");
            Assert.Equal("veyl_complete", Stage(reloaded, "Surveyor"));
            Assert.Equal(1, player.State.Inventory.CountOf("terrain_scanner"));
            Assert.NotEqual(0, reloaded.World.GetShape(site.Socket));
            Assert.True(reloaded.World.GetBlock(site.Contact + new Vector3i(0, 0, -1)).IsAir);
        }
    }

    [Fact]
    public void NetworkScanSpoof_WithoutEquipmentOrAtDistance_DoesNotRevealOrAward()
    {
        var server = Start(out var repo, out var client);
        using (repo)
        using (client)
        {
            var site = server.VeylSurveyForTest()!.Value;
            var player = server.AddLocalPlayer("Spoofer");
            StandAt(player, site.Surface + new Vector3i(12, 0, 0));
            int before = player.State.KnowledgePoints;
            client.Send(NetCodec.Encode(new ScanIntent { SubjectType = "block", SubjectKey = "rune_stone" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.01);
            Assert.Equal("veyl_signal", Stage(server, "Spoofer"));
            Assert.Equal(before, player.State.KnowledgePoints);
            StandAt(player, site.Surface);
            player.State.Inventory.Remove("hand_scanner", 1);
            client.Send(NetCodec.Encode(new ScanIntent { SubjectType = "block", SubjectKey = "rune_stone" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.01);
            Assert.Equal("veyl_signal", Stage(server, "Spoofer"));
            Assert.Equal(before, player.State.KnowledgePoints);
        }
    }

    [Fact]
    public void FullInventory_KeepsRewardPendingAndDoesNotDuplicateAfterSpaceIsFreed()
    {
        var server = Start(out var repo, out var client);
        using (repo)
        using (client)
        {
            var player = server.AddLocalPlayer("Full");
            player.State.Milestones.Add("survey:veyl:response"); // start at the settlement boundary under test
            StandInShip(server, player);
            for (int i = 0; i < player.State.Inventory.SlotCount; i++)
                player.State.Inventory.SetSlot(i, new ItemStack("stone", 1024));
            server.VeylSurveyHomecomingForTest("Full");
            server.VeylSurveyHomecomingForTest("Full");
            Assert.DoesNotContain("survey:veyl:rewarded", player.State.Milestones);
            Assert.Equal(0, player.State.Inventory.CountOf("terrain_scanner"));
            player.State.Inventory.SetSlot(0, null);
            server.VeylSurveyHomecomingForTest("Full");
            server.VeylSurveyHomecomingForTest("Full");
            Assert.Equal(1, player.State.Inventory.CountOf("terrain_scanner"));
            Assert.Contains("survey:veyl:rewarded", player.State.Milestones);
        }
    }

    [Fact]
    public void RepairedSite_IsReadableByAnotherExplorer_AndSharedStoryCreditSurvivesReload()
    {
        var server = Start(out var repo, out var client);
        int after;
        (Vector3i Surface, Vector3i Contact, Vector3i Socket) site;
        using (repo)
        using (client)
        {
            site = server.VeylSurveyForTest()!.Value;
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Excavate(server, alice, site.Contact, site.Socket);
            server.ScanSubject("Alice", "block", "rune_stone");
            alice.State.Inventory.Add("stone", 1, 1024);
            server.ShapeCraft("Alice", "stone", (int)BlockShape.Ramp);
            int before = Assert.Single(repo.ListStoryStates()).Milestones;
            server.PlaceBlock("Alice", site.Socket.X, site.Socket.Y, site.Socket.Z,
                ItemKey.Compose("stone", 0, 0, (int)BlockShape.Ramp), upFace: 2, yaw: 0);
            Assert.Equal("veyl_return", Stage(server, "Alice"));
            after = Assert.Single(repo.ListStoryStates()).Milestones;
            Assert.Equal(before + 1, after);
            int repairShape = server.World.GetShape(site.Socket);
            // A first-time co-op visitor reads the existing physical repair. No mining/replacement is needed.
            StandAt(bob, site.Contact);
            server.ScanSubject("Bob", "block", "rune_stone");
            server.ScanSubject("Bob", "block", "rune_stone");
            Assert.Equal("veyl_return", Stage(server, "Bob"));
            Assert.Equal(repairShape, server.World.GetShape(site.Socket));
            Assert.Equal(after, Assert.Single(repo.ListStoryStates()).Milestones);
            server.Stop();
        }
        var reloaded = Start(out var repo2, out var client2);
        using (repo2)
        using (client2)
        {
            Assert.Equal(after, Assert.Single(repo2.ListStoryStates()).Milestones);
            var visitor = reloaded.AddLocalPlayer("Later visitor");
            // A new player's first mapped system independently advances story; isolate the site's read.
            int beforeRead = Assert.Single(repo2.ListStoryStates()).Milestones;
            StandAt(visitor, site.Contact);
            reloaded.ScanSubject(visitor.State.PlayerId, "block", "rune_stone");
            Assert.Equal("veyl_return", Stage(reloaded, visitor.State.PlayerId));
            Assert.Equal(beforeRead, Assert.Single(repo2.ListStoryStates()).Milestones);
        }
    }

    [Fact]
    public void Homecoming_RequiresOwnHull_UsesOwnCargo_AndReplicatesSpecimenOnItsOwningVessel()
    {
        var transport = new RecordingTransport();
        var server = Start(out var repo, out var client, transport: transport);
        string starterId;
        using (repo)
        using (client)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            starterId = alice.ActiveShipId;
            Assert.Equal("default", starterId); // fresh and migrated single-ship saves share this stable id
            alice.State.Milestones.Add("survey:veyl:response");
            alice.Ships[starterId].Cargo.Add("iron_ore", 7, 99);
            bob.Ships[bob.ActiveShipId].Cargo.Add("gold_ore", 9, 99);
            StandInShip(server, alice, "Bob");
            server.VeylSurveyHomecomingForTest("Alice");
            Assert.False(alice.State.Inventory.Has("terrain_scanner", 1));
            Assert.DoesNotContain("survey:veyl:rewarded", alice.State.Milestones);

            StandInShip(server, alice);
            // The same hull is reachable across either seam of the torus.
            alice.State.Position = new Vector3f(alice.State.Position.X + server.World.Circumference,
                alice.State.Position.Y, alice.State.Position.Z - WorldConstants.LatitudePeriodFor(server.World.Circumference));
            // TickEnvironment can leave the served-player cursor on Bob immediately before the AI loop.
            server.CraftShip("Bob", "missing_ship");
            transport.Sent.Clear();
            server.VeylSurveyHomecomingForTest("Alice");
            Assert.Equal(1, alice.State.Inventory.CountOf("terrain_scanner"));
            var inventory = Assert.Single(transport.Sent.Where(p => p.Connection == alice.ConnectionId)
                .Select(p => p.Message).OfType<InventoryUpdate>());
            Assert.Contains(inventory.Cargo, s => s.Item == "iron_ore" && s.Count == 7);
            Assert.DoesNotContain(inventory.Cargo, s => s.Item == "gold_ore");
            Assert.Contains(transport.Sent, p => p.Connection == bob.ConnectionId
                && p.Message is LandedShipState { PlayerId: "Alice", HasVeylSpecimen: true });
            var specimen = Assert.Single(transport.Sent.Where(p => p.Connection == bob.ConnectionId)
                .Select(p => p.Message).OfType<LandedShipState>());
            var workshop = Assert.IsType<Vector3f>(server.StationPosition("workshop"));
            Assert.Equal(workshop.X - specimen.OriginX, specimen.SpecimenX);
            Assert.Equal(workshop.Y + 1f - specimen.OriginY, specimen.SpecimenY);
            Assert.Equal(workshop.Z - specimen.OriginZ, specimen.SpecimenZ);
            Assert.InRange(specimen.SpecimenX, 0, specimen.Width);
            Assert.InRange(specimen.SpecimenY, 0, specimen.Height + 1);
            Assert.InRange(specimen.SpecimenZ, 0, specimen.Length);
            Assert.DoesNotContain("survey:veyl:rewarded", bob.State.Milestones);

            alice.State.InstantBuild = true;
            alice.State.UnlockedBlueprints.Add("ship_hauler");
            var (ok, haulerId) = server.CraftShip("Alice", "hauler");
            Assert.True(ok);
            transport.Sent.Clear();
            Assert.True(server.SwitchShip(haulerId));
            Assert.Contains(transport.Sent, p => p.Connection == bob.ConnectionId
                && p.Message is LandedShipState { PlayerId: "Alice", HasVeylSpecimen: false });
            transport.Sent.Clear();
            Assert.True(server.SwitchShip(starterId));
            Assert.Contains(transport.Sent, p => p.Connection == bob.ConnectionId
                && p.Message is LandedShipState { PlayerId: "Alice", HasVeylSpecimen: true });
            server.Stop();
        }
        transport.Sent.Clear();
        var reloaded = Start(out var repo2, out var client2, transport: transport);
        using (repo2)
        using (client2)
        {
            var observer = reloaded.AddLocalPlayer("Bob");
            var owner = reloaded.AddLocalPlayer("Alice");
            Assert.Equal(starterId, owner.ActiveShipId);
            Assert.Contains(transport.Sent, p => p.Connection == observer.ConnectionId
                && p.Message is LandedShipState { PlayerId: "Alice", HasVeylSpecimen: true });
            Assert.Equal(1, owner.State.Inventory.CountOf("terrain_scanner"));
        }
    }

    [Fact]
    public void FlightAndStation_DoNotSettleReward_ButWalkingInsideOwnedShipInSpaceDoes()
    {
        var server = Start(out var repo, out var client, configure: config =>
        {
            config.Rules.FreeSpaceFlight = true;
            config.World = new WorldDescription { SpaceStations = Frequency.Frequent };
        });
        using (repo)
        using (client)
        {
            var player = server.AddLocalPlayer("Pilot");
            player.State.Milestones.Add("survey:veyl:response");
            StandInShip(server, player);
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));
            server.VeylSurveyHomecomingForTest("Pilot");
            Assert.False(player.State.Inventory.Has("terrain_scanner", 1));
            Assert.DoesNotContain(server.PlanetPoisForTest("Pilot"), p => p.Type == "veyl_return");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.SpaceStation);
            server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
            server.BoardStation("Pilot", station.Id);
            Assert.True(server.InStation("Pilot"));
            server.VeylSurveyHomecomingForTest("Pilot");
            Assert.False(player.State.Inventory.Has("terrain_scanner", 1));
            Assert.DoesNotContain(server.PlanetPoisForTest("Pilot"), p => p.Type == "veyl_return");
            server.LeaveStation("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));
            server.VeylSurveyHomecomingForTest("Pilot");
            Assert.Equal(1, player.State.Inventory.CountOf("terrain_scanner"));
            Assert.Contains("survey:veyl:specimen:" + player.ActiveShipId, player.State.Milestones);
        }
    }

    [Fact]
    public void UnplacedShip_DoesNotInventAReturnPosition_OrGrantARewardFromAboardFlag()
    {
        var server = Start(out var repo, out var client, ships: false);
        using (repo)
        using (client)
        {
            var player = server.AddLocalPlayer("No hull");
            player.State.Milestones.Add("survey:veyl:response");
            player.State.AboardShip = true;
            Assert.DoesNotContain(server.PlanetPoisForTest(player.State.PlayerId), p => p.Type == "veyl_return");
            server.VeylSurveyHomecomingForTest(player.State.PlayerId);
            Assert.False(player.State.Inventory.Has("terrain_scanner", 1));
        }
    }

    [Fact]
    public void Sandbox_DoesNotStampAnExpeditionOrGrantItsReward()
    {
        var server = Start(out var repo, out var client, story: false);
        using (repo)
        using (client)
        {
            Assert.Null(server.VeylSurveyForTest());
            var player = server.AddLocalPlayer("Sandbox");
            player.State.Milestones.Add("survey:veyl:response");
            server.VeylSurveyHomecomingForTest("Sandbox");
            Assert.False(player.State.Inventory.Has("terrain_scanner", 1));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
