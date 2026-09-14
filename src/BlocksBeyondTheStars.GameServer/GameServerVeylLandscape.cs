// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    private sealed record VeylLandscapePlan(VeylTerrainPlan? Terrain, int Prisms, int AddedCells, string Outcome);

    /// <summary>Propose bounded, grounded basalt prisms for a newly created site. Global planet generation
    /// remains unchanged. This reuses the survey terrace's actual-support/edit envelope and voxel writer,
    /// but only fills the small accepted masks; it never levels the area between the clusters.</summary>
    private VeylLandscapePlan PlanVeylLandscape(SettlementStructure structure, int geometryVersion,
        Vector3i origin, VeylTerrainPlan? terrace, List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        var planet = _world.Planet;
        if (!_worlds.Active.VirginAtLoad || planet.Void || planet.FloatingIslands)
            return new VeylLandscapePlan(null, 0, 0, "existing_world_or_unsupported_body");
        int surface = origin.Y + VeylSurveyGenerator.BurialDepthFor(geometryVersion);
        int centreX = origin.X + structure.Width / 2;
        var contact = structure.Markers.First(m => m.Type == "survey_surface").LocalPos + origin;
        int approachMinZ = terrace?.Min.Z ?? origin.Z - SurveyApproachLimit - 2;
        var min = terrace?.Min ?? origin + new Vector3i(-2, -MonumentPlinthDepth - 2, -2);
        var max = terrace?.Max ?? origin + new Vector3i(structure.Width + 1, structure.Height + 1, structure.Length + 1);
        var accepted = new Dictionary<(int X, int Z), VeylTerrainColumn>();
        int prisms = 0, addedCells = 0;
        bool wet = false;
        string lastRejected = "no_safe_cluster";
        long seed = _meta.Seed ^ WorldGenerator.StableHash(_world.LocationId + ":veyl_landscape_v1")
            ^ ((long)WorldConstants.WrapX(origin.X, _world.Circumference) << 32)
            ^ (uint)WorldConstants.WrapZ(origin.Z, _world.Circumference);
        foreach (var prism in VeylBasaltLandformGenerator.Propose(seed, structure.Width, structure.Length))
        {
            var supports = new List<(int X, int Z, int Support)>();
            int highest = int.MinValue, lowest = int.MaxValue;
            bool candidateWet = false, valid = true;
            foreach (var local in prism.Footprint)
            {
                int x = origin.X + local.X, z = origin.Z + local.Z;
                // Keep the full authored footprint, approach with shoulders, and inscription reading
                // area empty. The forward viewing corridor also keeps the Anchor visible from the path.
                if ((local.X >= -SurveyApron - 2 && local.X <= structure.Width + SurveyApron + 1
                        && local.Z >= -SurveyApron - 2 && local.Z <= structure.Length + SurveyApron + 1)
                    || (z >= approachMinZ && z <= origin.Z + 5 && Math.Abs(x - centreX) <= 4)
                    || (Math.Abs(x - contact.X) <= 6 && Math.Abs(z - contact.Z) <= 6)
                    || (local.Z <= 12 && local.X >= 4 && local.X <= structure.Width - 1))
                { lastRejected = "access_or_reading_keepout"; valid = false; break; }
                if (_generator.IsSurfaceLava(planet, x, z))
                { lastRejected = "lava"; valid = false; break; }
                int ground = _generator.SurfaceHeight(planet, x, z);
                int visibleBase = ground;
                if (_generator.TryGetWaterSurface(planet, x, z, out int water, out int seabed))
                {
                    ground = seabed;
                    visibleBase = water;
                    candidateWet = true;
                }
                int support = ground;
                while (support >= ground - 8 && _content.BlockById(_world.GetBlock(new Vector3i(x, support, z)))?.Solid != true)
                    support--;
                if (support < ground - 8)
                { lastRejected = "no_observed_support"; valid = false; break; }
                highest = Math.Max(highest, visibleBase);
                lowest = Math.Min(lowest, visibleBase);
                supports.Add((x, z, support));
            }
            if (!valid || supports.Count == 0) continue;
            // Keep coherent vertical prisms without enormous cliff-filling curtains. Their local tops
            // remain below the Anchor's upper landmark while retaining at least eight visible metres.
            int top = highest + prism.Height;
            if (highest - lowest > 8 || top > surface + 16)
            { lastRejected = "relief_or_landmark_height"; continue; }
            var candidate = new Dictionary<(int X, int Z), VeylTerrainColumn>(accepted);
            int candidateAdded = addedCells;
            var nextMin = min;
            var nextMax = max;
            foreach (var col in supports)
            {
                if (top - col.Support > VeylBasaltLandformGenerator.SupportDepthLimit)
                { lastRejected = "support_depth_budget"; valid = false; break; }
                var key = (col.X, col.Z);
                if (candidate.TryGetValue(key, out var previous))
                {
                    if (previous.FloorY >= top) continue;
                    candidateAdded -= previous.FloorY - previous.SupportY;
                }
                int support = previous is null ? col.Support : Math.Min(previous.SupportY, col.Support);
                candidate[key] = new VeylTerrainColumn(col.X, col.Z, support, top, top, 0);
                candidateAdded += top - support;
                if (candidateAdded > VeylBasaltLandformGenerator.AddedCellLimit)
                { lastRejected = "added_cell_budget"; valid = false; break; }
                nextMin = new Vector3i(Math.Min(nextMin.X, col.X - 2), Math.Min(nextMin.Y, support - 2), Math.Min(nextMin.Z, col.Z - 2));
                nextMax = new Vector3i(Math.Max(nextMax.X, col.X + 2), Math.Max(nextMax.Y, top + 2), Math.Max(nextMax.Z, col.Z + 2));
            }
            if (!valid) continue;
            int cx = (nextMin.X + nextMax.X) / 2, cz = (nextMin.Z + nextMax.Z) / 2;
            int hw = (nextMax.X - nextMin.X + 2) / 2, hl = (nextMax.Z - nextMin.Z + 2) / 2;
            if (reserved.Any(r => Math.Abs(WorldConstants.WrapDeltaX(cx - r.Cx, _world.Circumference)) < hw + r.Hw
                && Math.Abs(WorldConstants.WrapDeltaZ(cz - r.Cz, _world.Circumference)) < hl + r.Hl))
            { lastRejected = "content_reservation"; continue; }
            if (VeylTerrainHasPlayerEdits(nextMin, nextMax))
            { lastRejected = "player_edit"; continue; }
            accepted = candidate;
            addedCells = candidateAdded;
            min = nextMin;
            max = nextMax;
            wet |= candidateWet;
            prisms++;
        }
        // Sorting gives a stable stamp order independent of dictionary enumeration implementation.
        var columns = accepted.Values.OrderBy(c => c.X).ThenBy(c => c.Z).ToList();
        return new VeylLandscapePlan(columns.Count == 0 ? null : new VeylTerrainPlan(min, max, wet, columns),
            prisms, addedCells, prisms >= 6 ? "clustered" : "limited:" + lastRejected);
    }
}
