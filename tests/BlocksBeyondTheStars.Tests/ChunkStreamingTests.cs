// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Covers the chunk-streaming budget (A2) and the far-chunk eviction sweep (A4): the per-tick stream
/// budget is honoured (so a wider view fills proportionally faster), and chunks that drift outside every
/// player's keep-range are dropped from the cache while the player's own region stays resident.</summary>
public sealed class ChunkStreamingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ChunkStreamingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sweep_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private int ChunksLoadedAfterOneTick(string name, int budget, double timeBudgetMs = 0)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 4,
            ChunkStreamPerTick = budget,
            ChunkStreamBudgetMs = timeBudgetMs,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Streamer");
        int before = server.World.LoadedChunkCount;
        server.TickForTest(0.1); // exactly one streaming pass
        int delta = server.World.LoadedChunkCount - before;
        repo.Dispose();
        return delta;
    }

    [Fact]
    public void StreamBudget_ControlsHowFastTheViewFills()
    {
        // A bigger per-tick budget sends (and so caches) more new chunks in a single streaming pass — that is the
        // knob that keeps the wider default view from thawing in slowly at the horizon.
        int small = ChunksLoadedAfterOneTick("budget_small", 4);
        int large = ChunksLoadedAfterOneTick("budget_large", 20);

        Assert.True(small <= 4, $"one tick must not stream more than the budget (got {small} for budget 4)");
        Assert.True(large > small, $"a larger budget should fill faster (large={large}, small={small})");
    }

    [Fact]
    public void StreamTimeBudget_CutsAStreamingPassShort_ButAlwaysMakesProgress()
    {
        // The wall-clock budget exists for hosts whose tick shares the render thread (in-browser
        // singleplayer): a burst of synchronous first-visit generations must not stall the frame. A
        // near-zero budget is spent after the first send, so exactly one guaranteed chunk goes out —
        // while the same count budget without a time budget streams the full per-tick allowance.
        int unbudgeted = ChunksLoadedAfterOneTick("timebudget_off", 16);
        int budgeted = ChunksLoadedAfterOneTick("timebudget_on", 16, timeBudgetMs: 0.000001);

        Assert.True(budgeted >= 1, "a spent time budget must still stream at least one chunk (no starvation)");
        Assert.True(budgeted < unbudgeted, $"the time budget should cut the pass short (budgeted={budgeted}, unbudgeted={unbudgeted})");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Sweep_EvictsFarChunks_ButKeepsThePlayersOwnRegion()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sweep"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "sweep",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 2,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        // Use the player's natural safe spawn (set by the join-time spawn guard) so the runtime void-rescue never
        // relocates them mid-test — that would move the anchor and invalidate the assertions.
        var p = server.AddLocalPlayer("Wanderer");
        var nearChunk = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        // Stream the player's own region in (12 chunks/tick).
        for (int i = 0; i < 20; i++)
        {
            server.TickForTest(0.1);
        }

        Assert.True(server.World.IsChunkLoaded(nearChunk), "the player's own chunk should be resident");

        // Force a chunk far away into the cache (e.g. a query from another subsystem) — it sits well outside the
        // player's keep-range and is exactly what the sweep is meant to reclaim. Canonicalize it the way the
        // streamer/cache do (the world is a torus on BOTH axes), so the coord matches the key the sweep evicts —
        // SentChunks only ever holds canonical coords in production.
        var farChunk = WorldConstants.CanonicalChunk(
            new ChunkCoord(nearChunk.X, nearChunk.Y, nearChunk.Z + 40), server.World.Circumference);
        server.World.GetOrLoadChunk(farChunk);
        p.SentChunks.Add(farChunk); // pretend it was streamed to the player, so we can assert it gets forgotten
        Assert.True(server.World.IsChunkLoaded(farChunk), "the far chunk should be cached right after loading it");

        // Tick past the sweep interval (player stays put, so the near region is the anchor).
        for (int i = 0; i < 15; i++)
        {
            server.TickForTest(1.0);
        }

        Assert.False(server.World.IsChunkLoaded(farChunk), "the far chunk should have been swept out of the cache");
        Assert.DoesNotContain(farChunk, p.SentChunks); // forgotten too → it re-streams fresh if the player returns
        Assert.True(server.World.IsChunkLoaded(nearChunk), "the player's own region must stay resident through the sweep");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ClientViewDistance_ExtendsStreamingRadius_BeyondHostDefault()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vd"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "vd",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1, // small host default — the client asks for more
            ChunkStreamPerTick = 16,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        var p = server.AddLocalPlayer("FarSighted");
        p.ViewDistance = 3; // the client's slider, larger than the host's radius-1 default
        var center = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        for (int i = 0; i < 30; i++)
        {
            server.TickForTest(0.1); // fill the view; short dt so the 10 s sweep never trips
        }

        // A chunk 2 east is inside the client's radius-3 view but outside the host's radius-1 default — it is
        // resident only because the client's requested view distance drove the streaming radius.
        var withinClientView = new ChunkCoord(center.X + 2, center.Y, center.Z);
        // A chunk 5 east is beyond the client's radius-3 view AND its one-ring load-ahead margin (radius+1 = 4),
        // so it must never have been streamed. (#388's load-ahead reaches +4, not +5.)
        var beyondClientView = new ChunkCoord(center.X + 5, center.Y, center.Z);

        Assert.True(server.World.IsChunkLoaded(withinClientView), "client's wider view distance should stream terrain past the host default");
        Assert.False(server.World.IsChunkLoaded(beyondClientView), "nothing beyond the client's requested radius (plus the one-ring load-ahead) should stream");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void FarColumns_StreamOnlyTheSurfaceBand_WhileNearColumnsStreamTheFullVerticalSpan()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "lod"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "lod",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 5,   // radius 5 > the near-full-column radius (3), so there are "far" columns
            ChunkStreamPerTick = 64,  // drain the whole view quickly
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        var p = server.AddLocalPlayer("Surveyor");
        var center = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        for (int i = 0; i < 30; i++)
        {
            server.TickForTest(0.1); // short dt so the 10 s far-chunk sweep never trips
        }

        // The player's own column (dx=0) keeps the full 6-layer vertical span (-3..+2) for caves/digging.
        int nearLayers = 0;
        for (int cy = center.Y - 3; cy <= center.Y + 2; cy++)
        {
            if (server.World.IsChunkLoaded(new ChunkCoord(center.X, cy, center.Z))) nearLayers++;
        }

        // A far column (dx=5, Chebyshev 5 > 3) streams only the band around its surface — count over a wide
        // vertical window so we catch the band wherever the terrain there sits.
        int farLayers = 0;
        for (int cy = center.Y - 8; cy <= center.Y + 8; cy++)
        {
            if (server.World.IsChunkLoaded(new ChunkCoord(center.X + 5, cy, center.Z))) farLayers++;
        }

        Assert.Equal(6, nearLayers); // near column: full vertical span
        Assert.InRange(farLayers, 1, 3); // far column: just the surface band (below+surface+above)
    }

    [Theory]
    [InlineData(false, false)] // ordinary interior
    [InlineData(true, false)]  // longitude seam at X=0
    [InlineData(false, true)]  // latitude seam at its negative canonical boundary
    [InlineData(true, true)]   // both seams meet
    public void RepeatedSweeps_KeepStreamedNeighborsAndSentSetAcrossBothSeams_WhileEvictingFarChunks(
        bool longitudeSeam, bool latitudeSeam)
    {
        string name = $"stable_sweep_{longitudeSeam}_{latitudeSeam}";
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
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
            ChunkStreamPerTick = 64,
            Rules = new GameRules
            {
                StoryId = "none",
                CreatureAbundance = AlienActivity.Off,
                AggressiveAliens = AlienActivity.Off,
                PlanetEnemies = AlienActivity.Off,
                Bandits = AlienActivity.Off,
            },
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        try
        {
            var player = server.AddLocalPlayer("Stationary");
            int x = longitudeSeam ? 0 : 20 * WorldConstants.ChunkSize;
            int z = latitudeSeam ? -WorldConstants.LatitudePeriodFor(server.World.Circumference) / 2 : 0;
            const int feetY = 80;
            // A small stationary test foothold keeps the real void/entombment guard from moving the
            // anchor. Streaming and eviction still execute through ordinary server ticks and sent-sets.
            server.World.SetBlock(new Vector3i(x, feetY - 1, z), _content.GetBlock("stone")!.NumericId);
            for (int y = feetY; y <= feetY + 2; y++)
                server.World.SetBlock(new Vector3i(x, y, z), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            var position = new Vector3f(x + 0.5f, feetY + 0.05f, z + 0.5f);
            player.State.Position = position;
            Assert.False(server.IsInVoidForTest(position));
            Assert.False(server.IsEntombedForTest(position));
            for (int i = 0; i < 20; i++) server.TickForTest(0.1); // fill before the first 10-second sweep

            var center = WorldConstants.WorldToChunk(position.ToBlock());
            var west = WorldConstants.CanonicalChunk(new ChunkCoord(center.X - 1, center.Y, center.Z), server.World.Circumference);
            var north = WorldConstants.CanonicalChunk(new ChunkCoord(center.X, center.Y, center.Z - 1), server.World.Circumference);
            Assert.Contains(west, player.SentChunks);
            Assert.Contains(north, player.SentChunks);
            var westData = server.World.GetOrLoadChunk(west);
            var northData = server.World.GetOrLoadChunk(north);
            var sentBefore = new System.Collections.Generic.HashSet<ChunkCoord>(player.SentChunks);
            var farHorizontal = WorldConstants.CanonicalChunk(new ChunkCoord(center.X + 8, center.Y, center.Z), server.World.Circumference);
            var farVertical = new ChunkCoord(center.X, center.Y + 8, center.Z);
            Assert.DoesNotContain(farHorizontal, sentBefore);
            Assert.DoesNotContain(farVertical, sentBefore);

            for (int sweep = 0; sweep < 3; sweep++)
            {
                server.World.GetOrLoadChunk(farHorizontal);
                server.World.GetOrLoadChunk(farVertical);
                player.SentChunks.Add(farHorizontal);
                player.SentChunks.Add(farVertical);
                server.TickForTest(10.01);
                Assert.Equal(position, player.State.Position);
                Assert.True(server.World.IsChunkLoaded(west), "A visible western seam neighbor must not be swept away.");
                Assert.True(server.World.IsChunkLoaded(north), "A visible latitude seam neighbor must not be swept away.");
                Assert.Same(westData, server.World.GetOrLoadChunk(west));
                Assert.Same(northData, server.World.GetOrLoadChunk(north));
                Assert.True(sentBefore.SetEquals(player.SentChunks), "A stationary view must keep its sent-set, avoiding periodic full resends.");
                Assert.False(server.World.IsChunkLoaded(farHorizontal), "The wrap fix must still evict genuinely distant surface chunks.");
                Assert.False(server.World.IsChunkLoaded(farVertical), "Height must remain linear and contribute to eviction distance.");
                Assert.DoesNotContain(farHorizontal, player.SentChunks);
                Assert.DoesNotContain(farVertical, player.SentChunks);
            }
        }
        finally { server.Stop(); }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
