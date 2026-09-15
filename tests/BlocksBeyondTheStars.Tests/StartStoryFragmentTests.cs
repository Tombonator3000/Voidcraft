// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The bundled singleplayer host makes the active story's opening clue discoverable near spawn.</summary>
public sealed class StartStoryFragmentTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StartStoryFragmentTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_start_story_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void GuaranteedOpeningFragment_RevealsFirstVoidcraftSignalNearLandingPad()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "signal"));
        var transport = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "signal",
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            GuaranteeStartStoryFragment = true,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        try
        {
            server.AddLocalPlayer("Pilot");

            Assert.NotEmpty(server.LandingPadCenters);
            Assert.NotEmpty(server.NetFragmentSnapshots);
            var intro = server.NetFragmentSnapshots[0];
            Assert.Equal("frag_vega_return", intro.Key);

            var pad = server.LandingPadCenters[0];
            int circumference = server.World.Circumference;
            double dx = Math.Abs(intro.Pos.X - pad.X);
            dx = Math.Min(dx, circumference - dx);
            double dz = intro.Pos.Z - pad.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            Assert.InRange(distance, 12.0, 40.0);

            int beatsBefore = server.StorySnapshot.BeatsRevealed;
            Assert.True(server.PickUpNetFragmentForTest(intro.Id));
            Assert.Equal(beatsBefore + 1, server.StorySnapshot.BeatsRevealed);
        }
        finally
        {
            server.Stop();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (System.IO.Directory.Exists(_root))
            {
                System.IO.Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort temporary test cleanup.
        }
    }
}
