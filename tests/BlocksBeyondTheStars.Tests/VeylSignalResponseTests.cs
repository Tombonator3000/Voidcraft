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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

public sealed class VeylSignalResponseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_signal_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(out SqliteWorldRepository repo, RecordingTransport transport, bool story = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "signal"));
        var config = new ServerConfig
        {
            WorldName = "signal",
            Seed = 4242,
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
            ViewDistanceChunks = 1,
            Rules = new GameRules { StoryId = story ? "voidcraft_awakening" : "none" },
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly List<(int Connection, object Message, DeliveryMode Mode)> Sent = new();
        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } message) Sent.Add((connectionId, message, mode));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode) => Send(int.MinValue, payload, mode);
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; }
        public void Receive(int connectionId, object message) => PayloadReceived?.Invoke(connectionId, NetCodec.Encode(message));
        public void Stop() { }
        public void Dispose() { }
    }

    private static void StandAt(PlayerSession player, Vector3i core)
    {
        player.State.AboardShip = false;
        player.State.Position = new Vector3f(core.X + 0.5f, core.Y, core.Z - 2.5f);
    }

    private static void PrepareRepair(SvGameServer server, PlayerSession player)
    {
        var site = server.VeylSurveyForTest()!.Value;
        StandAt(player, site.Contact);
        for (int z = -2; z <= -1; z++)
            for (int y = 0; y <= 1; y++)
            {
                var p = site.Contact + new Vector3i(0, y, z);
                server.MineBlock(player.State.PlayerId, p.X, p.Y, p.Z);
                Assert.True(server.World.GetBlock(p).IsAir);
            }
        server.MineBlock(player.State.PlayerId, site.Socket.X, site.Socket.Y, site.Socket.Z);
        Assert.True(server.World.GetBlock(site.Socket).IsAir);
        server.ScanSubject(player.State.PlayerId, "block", "rune_stone");
        player.State.Inventory.Add("stone", 2, 1024);
        server.ShapeCraft(player.State.PlayerId, "stone", (int)BlockShape.Ramp);
    }

    private static void Repair(SvGameServer server, PlayerSession player)
    {
        var p = server.VeylSurveyForTest()!.Value.Socket;
        server.PlaceBlock(player.State.PlayerId, p.X, p.Y, p.Z,
            ItemKey.Compose("stone", 0, 0, (int)BlockShape.Ramp), upFace: 2, yaw: 0);
        Assert.Contains("survey:veyl:response", player.State.Milestones);
    }

    [Fact]
    public void FirstRepair_BroadcastsOnceAfterPermanentDeltas_OnlyToCurrentWorld_WithoutGrantOrReplay()
    {
        var transport = new RecordingTransport();
        var server = Start(out var repo, transport);
        using (repo)
        {
            try
            {
                var alice = server.AddLocalPlayer("Alice");
                var bob = server.AddLocalPlayer("Bob");
                var distant = server.AddLocalPlayer("Other body");
                distant.CurrentLocationId = "another_body";
                var site = server.VeylSurveyForTest()!.Value;
                // A client cannot request this server-only effect or gain a milestone by forging its DTO.
                transport.Sent.Clear();
                transport.Receive(alice.ConnectionId, new VeylSignalResponse { EventId = "forged" });
                Assert.DoesNotContain("survey:veyl:response", alice.State.Milestones);
                Assert.DoesNotContain(transport.Sent, m => m.Message is VeylSignalResponse);
                PrepareRepair(server, alice);
                // An ordinary supported cube is still not the shaped solution.
                server.PlaceBlock("Alice", site.Socket.X, site.Socket.Y, site.Socket.Z, "stone");
                Assert.DoesNotContain(transport.Sent, m => m.Message is VeylSignalResponse);
                server.MineBlock("Alice", site.Socket.X, site.Socket.Y, site.Socket.Z);
                transport.Sent.Clear();
                Repair(server, alice);
                var events = transport.Sent.Where(m => m.Message is VeylSignalResponse).ToArray();
                Assert.Equal(2, events.Length);
                Assert.Contains(events, m => m.Connection == alice.ConnectionId);
                Assert.Contains(events, m => m.Connection == bob.ConnectionId);
                Assert.All(events, m => Assert.Equal(DeliveryMode.ReliableOrdered, m.Mode));
                var response = Assert.IsType<VeylSignalResponse>(events[0].Message);
                Assert.Equal(response.EventId, Assert.IsType<VeylSignalResponse>(events[1].Message).EventId);
                Assert.InRange(response.Nodes.Length, 10, VeylSignalResponse.MaximumNodes);
                var ordered = transport.Sent.Where(m => m.Connection == alice.ConnectionId).Select(m => m.Message).ToList();
                int pulse = ordered.FindIndex(m => m is VeylSignalResponse);
                Assert.InRange(ordered.FindIndex(m => m is BlockChanged b && b.Y == site.Contact.Y && b.Glow == 0x66ECFF), 0, pulse - 1);
                Assert.False(alice.State.Inventory.Has("terrain_scanner", 1)); // Still earned only on homecoming.
                transport.Sent.Clear();
                StandAt(bob, site.Contact);
                server.ScanSubject("Bob", "block", "rune_stone");
                server.ScanSubject("Bob", "block", "rune_stone");
                server.ScanSubject("Alice", "block", "rune_stone");
                Assert.Contains("survey:veyl:response", bob.State.Milestones);
                Assert.DoesNotContain(transport.Sent, m => m.Message is VeylSignalResponse);
                distant.CurrentLocationId = alice.CurrentLocationId; // Restore a real location before persistence.
            }
            finally { server.Stop(); }
        }
        transport.Sent.Clear();
        var reloaded = Start(out var repo2, transport);
        using (repo2)
        {
            try
            {
                var visitor = reloaded.AddLocalPlayer("Later visitor");
                StandAt(visitor, reloaded.VeylSurveyForTest()!.Value.Contact);
                reloaded.ScanSubject(visitor.State.PlayerId, "block", "rune_stone");
                Assert.Contains("survey:veyl:response", visitor.State.Milestones);
                Assert.DoesNotContain(transport.Sent, m => m.Message is VeylSignalResponse);
            }
            finally { reloaded.Stop(); }
        }
    }

    [Fact]
    public void RepairPulse_OmitsRemovedCoveredAndReshapedAnchorRunes_WithoutWritingOtherBlocks()
    {
        var transport = new RecordingTransport();
        var server = Start(out var repo, transport);
        using (repo)
        {
            try
            {
                var player = server.AddLocalPlayer("Surveyor");
                PrepareRepair(server, player);
                var core = server.VeylSurveyForTest()!.Value.Contact;
                var origin = core - VeylSurveyGenerator.VaultBuriedContact;
                Vector3i Node(int y, int z) => WorldConstants.CanonicalBlock(origin + new Vector3i(12, y, z), server.World.Circumference);
                var removed = Node(12, 50); var covered = Node(13, 49); var shaped = Node(14, 49);
                server.World.SetBlock(removed, BlockId.Air);
                server.World.SetBlock(covered + new Vector3i(0, 0, -1), _content.GetBlock("stone")!.NumericId);
                server.World.SetBlock(shaped, _content.GetBlock("rune_stone")!.NumericId, 0, 0, ShapeCode.Pack(BlockShape.Stairs, 0));
                transport.Sent.Clear();
                Repair(server, player);
                var response = Assert.Single(transport.Sent.Select(m => m.Message).OfType<VeylSignalResponse>());
                var positions = response.Nodes.Select(n => new Vector3i(n.X, n.Y, n.Z)).ToArray();
                Assert.DoesNotContain(removed, positions); Assert.DoesNotContain(covered, positions); Assert.DoesNotContain(shaped, positions);
                Assert.Equal(positions.Length, positions.Distinct().Count());
                Assert.All(positions, p =>
                {
                    Assert.Equal("rune_stone", _content.BlockById(server.World.GetBlock(p))!.Key);
                    Assert.Equal(0, ShapeCode.ShapeOf(server.World.GetShape(p)));
                    Assert.True(server.World.GetBlock(p + new Vector3i(0, 0, -1)).IsAir);
                    Assert.Equal(p, WorldConstants.CanonicalBlock(p, server.World.Circumference));
                });
                // The only changed voxels are the player's socket and the original two permanent contacts.
                var changed = transport.Sent.Select(m => m.Message).OfType<BlockChanged>().ToArray();
                Assert.Equal(3, changed.Length);
                Assert.True(server.World.GetBlock(removed).IsAir);
            }
            finally { server.Stop(); }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SignalRoute_UsesExistingFullRuneFaces_AndLegacySitesHaveOnlyTheirContacts(int version)
    {
        var structure = VeylSurveyGenerator.Generate(_content, "stone", version);
        var nodes = VeylSurveyGenerator.SignalNodes(version).ToArray();
        Assert.InRange(nodes.Length, 2, VeylSignalResponse.MaximumNodes);
        Assert.Equal(nodes.Length, nodes.Distinct().Count());
        Assert.All(nodes, p =>
        {
            Assert.Equal(_content.GetBlock("rune_stone")!.NumericId.Value, structure.Get(p.X, p.Y, p.Z));
            Assert.Equal(0, ShapeCode.ShapeOf(structure.GetShape(p.X, p.Y, p.Z)));
        });
        if (version <= 1)
            Assert.Equal(new[] { VeylSurveyGenerator.BuriedContact, VeylSurveyGenerator.SurfaceContact }, nodes);
        else
        {
            Assert.Equal(VeylSurveyGenerator.VaultBuriedContact, nodes[0]);
            Assert.Contains(new Vector3i(12, 16, 48), nodes);
            Assert.DoesNotContain(new Vector3i(12, 11, 50), nodes); // Shaped pointed end.
            Assert.DoesNotContain(new Vector3i(12, 22, 50), nodes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void PinnedWrappedSite_EmitsCanonicalSurvivingNodes_WithoutMigratingItsGeometry(int version)
    {
        var transport = new RecordingTransport();
        var initial = Start(out var repo, transport, story: false);
        using (repo)
        {
            var template = VeylSurveyGenerator.Generate(_content, "stone", version);
            var origin = new Vector3i(initial.World.Circumference - 8, 40,
                WorldConstants.LatitudePeriodFor(initial.World.Circumference) / 2 - 8);
            repo.RunInTransaction(() =>
            {
                for (int x = 0; x < template.Width; x++)
                    for (int y = 0; y < template.Height; y++)
                        for (int z = 0; z < template.Length; z++)
                        {
                            var (tint, glow) = template.GetModifier(x, y, z);
                            initial.World.SetBlock(origin + new Vector3i(x, y, z),
                                new BlockId(template.Get(x, y, z)), tint, glow, template.GetShape(x, y, z));
                        }
            });
            initial.Stop();
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
        var loaded = Start(out var repo2, transport);
        using (repo2)
        {
            try
            {
                var player = loaded.AddLocalPlayer("Wrapped explorer");
                PrepareRepair(loaded, player);
                transport.Sent.Clear();
                Repair(loaded, player);
                var response = Assert.Single(transport.Sent.Select(m => m.Message).OfType<VeylSignalResponse>());
                Assert.Equal(version == 0 ? 2 : 20, response.Nodes.Length);
                Assert.All(response.Nodes, n =>
                {
                    var p = new Vector3i(n.X, n.Y, n.Z);
                    Assert.Equal(p, WorldConstants.CanonicalBlock(p, loaded.World.Circumference));
                    Assert.Equal("rune_stone", _content.BlockById(loaded.World.GetBlock(p))!.Key);
                });
                Assert.Equal(version, Assert.Single(loaded.PlacementRecordsForTest.Where(r => r.Kind == "veyl_survey")).GeometryVersion);
            }
            finally { loaded.Stop(); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalCodec_RoundTripsBoundedCanonicalNodes(bool json)
    {
        var source = new VeylSignalResponse
        {
            EventId = "planet|veyl_survey|0|5|-1488|1",
            Nodes = new[] { new VeylSignalNode { X = 5999, Y = -7, Z = -1488 }, new VeylSignalNode { X = 0, Y = 14, Z = 1487 } },
        };
        var bytes = json ? NetCodec.EncodeJson(source) : NetCodec.Encode(source);
        if (!json) Assert.Equal(203, bytes[0]);
        var decoded = Assert.IsType<VeylSignalResponse>(NetCodec.Decode(bytes));
        Assert.Equal(source.EventId, decoded.EventId);
        Assert.Equal(source.Nodes.Select(n => (n.X, n.Y, n.Z)), decoded.Nodes.Select(n => (n.X, n.Y, n.Z)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
