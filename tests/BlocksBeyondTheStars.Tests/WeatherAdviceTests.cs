// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Advice must describe the same per-player exposure as the effect tick, including the
/// beneficial ion effect and global scan penalty that a roof cannot remove.</summary>
public sealed class WeatherAdviceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_weather_advice_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repositories = new();

    private SvGameServer Start(bool hazards = true, LoopbackLink? link = null)
    {
        var name = "weather" + _repositories.Count;
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repositories.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 4242, StartPlanet = "varied", PlaceStarterShip = false, AutoSaveIntervalMinutes = 9999 };
        if (!hazards) config.ApplyCommandLine(new[] { "--hazards", "off" });
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(link ?? new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static PlayerState Outside(SvGameServer server, string id = "Explorer", int x = 500)
    {
        var p = server.AddLocalPlayer(id).State;
        p.Position = new Vector3f(x, 150f, 500f);
        p.AboardShip = false;
        return p;
    }

    private void Roof(SvGameServer server, int x = 500)
        => server.World.SetBlock(new Vector3i(x, 154, 500), _content.GetBlock("stone")!.NumericId);

    private static WorldEnvironment Advice(SvGameServer server, PlayerState p)
        => Assert.IsType<WorldEnvironment>(server.WeatherAdviceForTest(p.PlayerId));

    [Fact]
    public void OpenCorrosiveWeatherOffersARoofThenAnUrgentReturnAsTheSuitRunsOut()
    {
        var server = Start();
        var p = Outside(server);
        server.SetWeatherForTest("acid_rain");
        p.SuitEnergy = 80f;
        var healthy = Advice(server, p);
        Assert.Equal("weather.advice.build_roof", healthy.WeatherAdviceKey);
        Assert.Equal("weather.protection.open", healthy.WeatherProtectionKey);
        p.SuitEnergy = 20f;
        var low = Advice(server, p);
        Assert.Equal("weather.advice.return", low.WeatherAdviceKey);
        Assert.True(low.WeatherAdviceUrgency > healthy.WeatherAdviceUrgency);
        p.SuitEnergy = 0f;
        Assert.Equal("weather.advice.empty", Advice(server, p).WeatherAdviceKey);
        float health = p.Health;
        server.Tick(0.5);
        Assert.True(p.Health < health, "The urgent advice corresponds to a live damaging effect.");
    }

    [Fact]
    public void TwoPlayersInOneStormReceiveDifferentProtectionAndEffect()
    {
        var server = Start();
        var covered = Outside(server, "Covered");
        var open = Outside(server, "Open", 504);
        Roof(server);
        server.SetWeatherForTest("acid_rain");
        covered.SuitEnergy = open.SuitEnergy = 70f;
        Assert.Equal("weather.advice.covered", Advice(server, covered).WeatherAdviceKey);
        Assert.Equal("weather.advice.build_roof", Advice(server, open).WeatherAdviceKey);
        for (int i = 0; i < 10; i++) server.Tick(0.1);
        Assert.True(covered.SuitEnergy > open.SuitEnergy, "One player's roof cannot protect the other player.");
        Assert.Equal("weather.protection.roof", Advice(server, covered).WeatherProtectionKey);
    }

    [Fact]
    public void RoofChangesIonChargingButNeverRestoresGlobalTerrainScanRange()
    {
        var server = Start(hazards: false);
        var p = Outside(server);
        server.SetWeatherForTest("ion_storm");
        p.SuitEnergy = 40f;
        var exposed = Advice(server, p);
        Assert.Equal("weather.advice.ion_open", exposed.WeatherAdviceKey);
        server.Tick(0.5);
        Assert.True(p.SuitEnergy > 40f, "The existing ion benefit applies even when damaging hazards are disabled.");
        Roof(server);
        float energy = p.SuitEnergy;
        var covered = Advice(server, p);
        Assert.Equal("weather.advice.ion_covered", covered.WeatherAdviceKey);
        Assert.Equal(exposed.TerrainScanWeatherFactor, covered.TerrainScanWeatherFactor);
        Assert.True(covered.TerrainScanWeatherFactor < 1f);
        server.Tick(0.5);
        Assert.Equal(energy, p.SuitEnergy);
    }

    [Fact]
    public void DisabledHazardsAndGodModeDoNotRecommendUnnecessaryEmergencyShelter()
    {
        var server = Start(hazards: false);
        var p = Outside(server);
        server.SetWeatherForTest("acid_rain");
        p.SuitEnergy = 0f;
        Assert.Equal("weather.advice.rules", Advice(server, p).WeatherAdviceKey);
        Assert.Equal(0, Advice(server, p).WeatherAdviceUrgency);
        float health = p.Health;
        server.Tick(0.5);
        Assert.Equal(health, p.Health);
        p.GodMode = true;
        Assert.Equal("weather.advice.immune", Advice(server, p).WeatherAdviceKey);
        p.GodMode = false;
        p.InEva = true;
        Assert.Empty(Advice(server, p).WeatherAdviceKey);
        p.InEva = false;
        p.Health = 0f;
        Assert.Empty(Advice(server, p).WeatherAdviceKey);
    }

    [Fact]
    public void ShipProtectionAndFullIonSuitAreHonestAlternativesToTheOpenWeatherAdvice()
    {
        var server = Start();
        var p = Outside(server);
        server.SetWeatherForTest("acid_rain");
        p.AboardShip = true;
        Assert.Equal("weather.advice.interior", Advice(server, p).WeatherAdviceKey);
        Assert.Equal("weather.protection.ship", Advice(server, p).WeatherProtectionKey);
        p.AboardShip = false;
        server.SetWeatherForTest("ion_storm");
        p.SuitEnergy = 100f;
        Assert.Equal("weather.advice.ion_full", Advice(server, p).WeatherAdviceKey);
        server.SetWeatherForTest("clear");
        Assert.Empty(Advice(server, p).WeatherAdviceKey);
    }

    [Fact]
    public void AdviceSurvivesTheExistingEnvironmentWireContractAndHasBothLanguages()
    {
        var server = Start();
        var p = Outside(server);
        server.SetWeatherForTest("ion_storm");
        var before = Advice(server, p);
        var after = Assert.IsType<WorldEnvironment>(NetCodec.Decode(NetCodec.Encode(before)));
        Assert.Equal(before.WeatherAdviceKey, after.WeatherAdviceKey);
        Assert.Equal(before.WeatherProtectionKey, after.WeatherProtectionKey);
        Assert.Equal(before.WeatherAdviceUrgency, after.WeatherAdviceUrgency);
        Assert.Equal(before.TerrainScanWeatherFactor, after.TerrainScanWeatherFactor);
        Assert.Equal(1f, new WorldEnvironment().TerrainScanWeatherFactor);
        foreach (var language in new[] { "en", "de" })
        {
            var table = TestLocales.Load(language);
            foreach (var key in new[] { before.WeatherAdviceKey, before.WeatherProtectionKey, "weather.advice.scan_range", "weather.advice.climate_return" })
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"Missing {language}: {key}");
        }
    }

    [Theory]
    [InlineData("ion_storm", false)]
    [InlineData("ion_storm", true)]
    [InlineData("blizzard", true)]
    public void LowSuitAndActualClimateDrainTakePriorityOverContinueOrChargeAdvice(string weather, bool roof)
    {
        var server = Start();
        var p = Outside(server);
        if (roof) Roof(server);
        server.SetWeatherForTest(weather);
        p.SuitEnergy = 20f;
        p.SuitClimateActive = true; // the temperature system's replicated current contribution
        var advice = Advice(server, p);
        Assert.Equal("weather.advice.climate_return", advice.WeatherAdviceKey);
        Assert.Equal(2, advice.WeatherAdviceUrgency);
        Assert.True(advice.TerrainScanWeatherFactor < 1f);
    }

    [Fact]
    public void RoofChangeIsDeliveredPromptlyOnceWithoutRepeatingUnchangedAdviceEveryTick()
    {
        var link = new LoopbackLink();
        var server = Start(link: link);
        using var client = new LoopbackClientTransport(link);
        var received = new List<WorldEnvironment>();
        client.PayloadReceived += payload => { if (NetCodec.Decode(payload) is WorldEnvironment env) received.Add(env); };
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Explorer" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        var p = server.Sessions[1].State;
        p.Position = new Vector3f(500, 150, 500);
        p.AboardShip = false;
        server.SetWeatherForTest("acid_rain");
        received.Clear();
        server.Tick(0.6);
        client.Poll();
        Assert.Contains(received, e => e.WeatherAdviceKey == "weather.advice.build_roof");
        received.Clear();
        server.Tick(0.6);
        client.Poll();
        Assert.Empty(received);
        Roof(server);
        server.Tick(0.6);
        client.Poll();
        Assert.Equal("weather.advice.covered", Assert.Single(received).WeatherAdviceKey);
    }

    public void Dispose()
    {
        foreach (var repo in _repositories) repo.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
