// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    private VeylSignalResponse CreateVeylSignalResponse(MonumentInstance monument)
    {
        var nodes = new List<VeylSignalNode>();
        if (monument.BuriedContact is not { } core) return new VeylSignalResponse();
        int version = FindPlacementRecord(SurveyKind, 0)?.GeometryVersion ?? 0;
        var localCore = version <= 1 ? VeylSurveyGenerator.BuriedContact : VeylSurveyGenerator.VaultBuriedContact;
        var origin = core - localCore;
        var canonicalCore = WorldConstants.CanonicalBlock(core, _world.Circumference);
        var seen = new HashSet<Vector3i>();
        foreach (var local in VeylSurveyGenerator.SignalNodes(version))
        {
            var p = WorldConstants.CanonicalBlock(origin + local, _world.Circumference);
            if (!seen.Add(p) || _content.BlockById(_world.GetBlock(p))?.Key != "rune_stone"
                || ShapeCode.ShapeOf(_world.GetShape(p)) != 0
                || !_world.GetBlock(p + new Vector3i(0, 0, -1)).IsAir) continue;
            nodes.Add(new VeylSignalNode { X = p.X, Y = p.Y, Z = p.Z });
            if (nodes.Count == VeylSignalResponse.MaximumNodes) break;
        }
        return new VeylSignalResponse
        {
            EventId = $"{_world.LocationId}|veyl_survey|{canonicalCore.X}|{canonicalCore.Y}|{canonicalCore.Z}|1",
            Nodes = nodes.ToArray(),
        };
    }
}
