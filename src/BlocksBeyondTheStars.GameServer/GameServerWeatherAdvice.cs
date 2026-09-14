// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    /// <summary>The existing weather effect resolved once from authoritative state. Advice and the
    /// effect tick share this query; it neither alters weather nor grants any new protection.</summary>
    private readonly struct WeatherExposure
    {
        public readonly string State, Protection;
        public readonly float Intensity, SuitDrain, HealthDrain, IonCharge;
        public WeatherExposure(string state, string protection, float intensity = 0,
            float suitDrain = 0, float healthDrain = 0, float ionCharge = 0)
        {
            State = state; Protection = protection; Intensity = intensity;
            SuitDrain = suitDrain; HealthDrain = healthDrain; IonCharge = ionCharge;
        }
    }

    private WeatherExposure ReadWeatherExposure(PlayerSession session)
    {
        var p = session.State;
        // Preserve the exclusions in ApplyWeatherToPlayer. Ion charging deliberately does NOT use
        // TemperatureHazardsEnabled: that rule suppresses damage, not the existing charge benefit.
        if (p.Health <= 0f) return new WeatherExposure(string.Empty, "inactive");
        if (p.InEva) return new WeatherExposure(string.Empty, "space");
        if (p.GodMode) return new WeatherExposure(string.Empty, "immune");
        if (InStation(p.PlayerId)) return new WeatherExposure(string.Empty, "station");
        if (p.AboardShip) return new WeatherExposure(string.Empty, "ship");
        var (state, intensity) = BiomeWeatherAt(p.Position);
        if (intensity <= 0.01f) return new WeatherExposure(state, "inactive", intensity);
        bool roofed = RoofedAt(p.Position); // existing loaded-column check; do not invent a client roof rule
        if (state == "ion_storm")
            return new WeatherExposure(state, roofed ? "roof" : "open", intensity,
                ionCharge: roofed ? 0 : IonChargePerSecond * intensity);
        if (!Rules.TemperatureHazardsEnabled) return new WeatherExposure(state, "rules", intensity);
        if (roofed) return new WeatherExposure(state, "roof", intensity);
        float bite = state switch { "acid_rain" => 1f, "ember_fall" => 0.75f, "meteor_shower" => 0.6f, _ => 0f };
        float scale = bite * intensity * Rules.HazardSeverityFactor;
        return new WeatherExposure(state, "open", intensity,
            WeatherSuitDrainPerSecond * scale, WeatherDamagePerSecond * scale);
    }

    private readonly struct WeatherAdvice
    {
        public readonly string Key, ProtectionKey;
        public readonly byte Urgency;
        public readonly float TerrainScanFactor;
        public WeatherAdvice(string key, string protectionKey, byte urgency, float terrainScanFactor)
        { Key = key; ProtectionKey = protectionKey; Urgency = urgency; TerrainScanFactor = terrainScanFactor; }
        public string Signature(string location) => location + ":" + Key + ":" + ProtectionKey + ":" + Urgency
            + ":" + Math.Round(TerrainScanFactor * 100);
    }

    private WeatherAdvice ReadWeatherAdvice(PlayerSession session)
    {
        var exposure = ReadWeatherExposure(session);
        float scan = (float)WeatherScanFactor(); // global gadget radius, independent of local roof/biome
        string protection = "weather.protection." + exposure.Protection;
        string key = string.Empty;
        byte urgency = 0;
        bool harmful = exposure.State is "acid_rain" or "ember_fall" or "meteor_shower";
        bool relevant = harmful || exposure.State == "ion_storm" || scan < 0.99f;
        switch (exposure.Protection)
        {
            case "inactive":
                if (session.State.Health > 0f && scan < 0.99f) key = "weather.advice.short_scan";
                break;
            case "space": break;
            case "immune":
                if (relevant || _weatherState is "acid_rain" or "ember_fall" or "meteor_shower" or "ion_storm")
                    key = "weather.advice.immune";
                break;
            case "ship":
            case "station":
                if (relevant || _weatherState is "acid_rain" or "ember_fall" or "meteor_shower" or "ion_storm")
                    key = "weather.advice.interior";
                break;
            case "rules":
                if (harmful) key = "weather.advice.rules";
                else if (scan < 0.99f) key = "weather.advice.short_scan";
                break;
            case "roof":
                if (harmful) key = "weather.advice.covered";
                else if (exposure.State == "ion_storm") key = "weather.advice.ion_covered";
                else if (scan < 0.99f) key = "weather.advice.short_scan";
                break;
            case "open":
                if (exposure.SuitDrain > 0 || exposure.HealthDrain > 0)
                {
                    bool low = session.State.SuitEnergy <= 25f;
                    urgency = (byte)(low ? 2 : 1);
                    key = session.State.SuitEnergy <= 0f ? "weather.advice.empty"
                        : low ? "weather.advice.return" : "weather.advice.build_roof";
                }
                else if (exposure.IonCharge > 0)
                    key = session.State.SuitEnergy >= 100f ? "weather.advice.ion_full" : "weather.advice.ion_open";
                else if (scan < 0.99f) key = "weather.advice.short_scan";
                break;
        }
        // A roof removes falling-weather damage but only reduces temperature exposure. Likewise ion
        // charging may lose to climate-control drain: never label a depleted, climate-stressed suit safe.
        if (session.State.Health > 0f && session.State.SuitEnergy <= 25f && session.State.SuitClimateActive
            && Rules.TemperatureHazardsEnabled && exposure.Protection is "open" or "roof" or "inactive")
        {
            key = "weather.advice.climate_return";
            urgency = 2;
        }
        return new WeatherAdvice(key, protection, urgency, scan);
    }

    private static void ApplyWeatherAdvice(WorldEnvironment environment, WeatherAdvice advice)
    {
        environment.WeatherAdviceKey = advice.Key;
        environment.WeatherProtectionKey = advice.ProtectionKey;
        environment.WeatherAdviceUrgency = advice.Urgency;
        environment.TerrainScanWeatherFactor = advice.TerrainScanFactor;
    }

    /// <summary>Bounded, per-player changed-only feedback after crossing a roof edge, using a roof edit,
    /// running low on charge or changing world rules. The ordinary five-second heartbeat remains.</summary>
    private void TickWeatherAdvice(PlayerSession session, double dt)
    {
        session.WeatherAdviceCheckIn -= dt;
        if (session.WeatherAdviceCheckIn > 0) return;
        session.WeatherAdviceCheckIn = 0.5;
        var advice = ReadWeatherAdvice(session);
        if (session.LastWeatherAdviceSignature != advice.Signature(session.CurrentLocationId)) SendEnvironment(session);
    }

    /// <summary>Inspection seam for the exact per-player advice carried by an environment update.</summary>
    public WorldEnvironment? WeatherAdviceForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null) return null;
        Serve(session);
        var env = BuildEnvironment(session.State.Position);
        ApplyWeatherAdvice(env, ReadWeatherAdvice(session));
        return env;
    }
}
