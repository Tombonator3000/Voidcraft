// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Crossing either periodic seam must preserve the real parked cabin's protection for its
/// owner and visitors, without expanding the existing rectangular envelope to exterior edits.</summary>
public sealed class ShipInteriorSeamTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_ship_seam_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "seam"));
        var config = new ServerConfig
        {
            WorldName = "seam",
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
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(-1, -1, false)]
    [InlineData(-1, 0, true)]
    [InlineData(0, -1, true)]
    [InlineData(1, 1, true)]
    public void OwnedAndVisitedCabinsKeepAboardAndWeatherProtectionAcrossSeams(int longitudeTurns, int latitudeTurns, bool visitor)
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            var guest = server.AddLocalPlayer("Guest");
            var (origin, _) = server.LandedShipBoundsForTest("Owner");
            Assert.NotEqual(origin, server.LandedShipBoundsForTest("Guest").Origin);
            var p = (visitor ? guest : owner).State;
            int x = origin.X + 3 + longitudeTurns * server.World.Circumference;
            int z = origin.Z + 6 + latitudeTurns * WorldConstants.LatitudePeriodFor(server.World.Circumference);
            Assert.True(server.ShipInteriorContainsCellForTest(x, origin.Y + 1, z));
            p.Position = new Vector3f(x + 0.5f, origin.Y + 1f, z + 0.5f);
            p.AboardShip = false; // the actual position query must establish protection
            p.SuitEnergy = 70f;
            float health = p.Health;
            server.SetWeatherForTest("acid_rain");
            for (int i = 0; i < 10; i++) server.Tick(0.1);
            Assert.True(p.AboardShip);
            Assert.Equal(health, p.Health);
            Assert.True(p.SuitEnergy >= 70f, "A protected cabin must not consume suit energy in acid rain.");
            var advice = Assert.IsType<WorldEnvironment>(server.WeatherAdviceForTest(p.PlayerId));
            Assert.Equal("weather.protection.ship", advice.WeatherProtectionKey);
            Assert.Equal("weather.advice.interior", advice.WeatherAdviceKey);
            server.Stop();
        }
    }

    [Fact]
    public void WrappedQueriesKeepExistingEnvelopeAndDoNotProtectExteriorOverhangs()
    {
        var server = Start(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Owner");
            var (origin, size) = server.LandedShipBoundsForTest("Owner");
            // A real persisted exterior edit does not expand the authored cabin's protection envelope.
            repo.SetStructureBlock("ship:Owner", new Vector3i(size.X + 1, 3, 6), _content.GetBlock("iron_wall")!.NumericId.Value);
            Assert.True(server.SwitchShip(server.ActiveShipId));
            Assert.False(server.BuildShipStructureForTest("Owner").Get(new Vector3i(size.X + 1, 3, 6)).IsAir);
            int x = origin.X + server.World.Circumference;
            int z = origin.Z - WorldConstants.LatitudePeriodFor(server.World.Circumference);
            Assert.True(server.ShipInteriorContainsCellForTest(x, origin.Y, z));
            Assert.True(server.ShipInteriorContainsCellForTest(x + size.X - 1, origin.Y + size.Y, z + size.Z - 1));
            Assert.False(server.ShipInteriorContainsCellForTest(x - 1, origin.Y + 1, z + 6));
            Assert.False(server.ShipInteriorContainsCellForTest(x + size.X, origin.Y + 1, z + 6));
            Assert.False(server.ShipInteriorContainsCellForTest(x + size.X + 1, origin.Y + 1, z + 6));
            Assert.False(server.ShipInteriorContainsCellForTest(x + 3, origin.Y + 1, z - 1));
            Assert.False(server.ShipInteriorContainsCellForTest(x + 3, origin.Y + 1, z + size.Z));
            Assert.False(server.ShipInteriorContainsCellForTest(x + 3, origin.Y - 1, z + 6));
            Assert.False(server.ShipInteriorContainsCellForTest(x + 3, origin.Y + size.Y + 1, z + 6));
            server.Stop();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
