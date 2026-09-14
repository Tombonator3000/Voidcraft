// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    private readonly Dictionary<int, (int Width, int Length)> _surveyDimensions = new();

    private bool OverlapsPinnedVeyl(int cx, int cz, int hw, int hl, int margin)
    {
        var record = FindPlacementRecord(SurveyKind, 0);
        if (record?.Placed != true) return false;
        Vector3i min, max;
        var bounds = record.Reservation;
        if (bounds?.MinX is int minX && bounds.MinY is int minY && bounds.MinZ is int minZ
            && bounds.MaxX is int maxX && bounds.MaxY is int maxY && bounds.MaxZ is int maxZ
            && minX <= maxX && minY <= maxY && minZ <= maxZ
            && (long)maxX - minX < _world.Circumference
            && (long)maxZ - minZ < WorldConstants.LatitudePeriodFor(_world.Circumference))
        {
            min = new Vector3i(minX, minY, minZ);
            max = new Vector3i(maxX, maxY, maxZ);
        }
        else
        {
            if (!_surveyDimensions.TryGetValue(record.GeometryVersion, out var dimensions))
            {
                var structure = VeylSurveyGenerator.Generate(_content, "stone", record.GeometryVersion);
                dimensions = (structure.Width, structure.Length);
                _surveyDimensions.Add(record.GeometryVersion, dimensions);
            }
            min = new Vector3i(record.X, record.GroundY, record.Z);
            max = min + new Vector3i(dimensions.Width - 1, 0, dimensions.Length - 1);
        }
        return Math.Abs(WorldConstants.WrapDeltaX(cx - (min.X + max.X) / 2, _world.Circumference))
                < hw + (max.X - min.X + 2) / 2 + margin
            && Math.Abs(WorldConstants.WrapDeltaZ(cz - (min.Z + max.Z) / 2, _world.Circumference))
                < hl + (max.Z - min.Z + 2) / 2 + margin;
    }

    private void AddVeylContentReservations(List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        foreach (var factory in _factories)
            reserved.Add(((factory.Min.X + factory.Max.X) / 2, (factory.Min.Z + factory.Max.Z) / 2,
                (factory.Max.X - factory.Min.X + 2) / 2, (factory.Max.Z - factory.Min.Z + 2) / 2));
        if (_wreck is not null)
            reserved.Add((_wreckOrigin.X + _wreck.Width / 2, _wreckOrigin.Z + _wreck.Length / 2,
                (_wreck.Width + 1) / 2, (_wreck.Length + 1) / 2));
        foreach (var vault in _vaultEntrances) reserved.Add((vault.X, vault.Z, 6, 6));
        foreach (var container in _containers) reserved.Add((container.Position.X, container.Position.Z, 2, 2));
        foreach (var cube in _dataCubes) reserved.Add(((int)Math.Floor(cube.Pos.X), (int)Math.Floor(cube.Pos.Z), 2, 2));
        foreach (var fragment in _netFragments) reserved.Add(((int)Math.Floor(fragment.Pos.X), (int)Math.Floor(fragment.Pos.Z), 2, 2));
    }

    private const int SurveyApron = 3;
    private const int SurveyApproachLimit = 64;
    private const int SurveyReliefLimit = 96;
    private const int SurveyTerrainOperationLimit = 250000;

    private sealed record VeylTerrainColumn(int X, int Z, int SupportY, int FloorY, int ClearY, int Shape);
    private sealed record VeylTerrainPlan(Vector3i Min, Vector3i Max, bool Wet, List<VeylTerrainColumn> Columns);

    /// <summary>Only a never-materialised body may receive a terrain-adapted survey site. Reuse the
    /// guaranteed structure search's deterministic ranking, then validate the complete support/approach
    /// volume. An ocean seat is a sealed stone foundation to the seabed, never a floating vault or a
    /// submerged stair. Rejected footprints are reserved before the next bounded search.</summary>
    private bool TryPlaceVeylTerrace(SettlementStructure structure, int version,
        List<(int Cx, int Cz, int Hw, int Hl)> reserved, out Vector3i origin, out int ground,
        out string seat, out VeylTerrainPlan? terrain)
    {
        origin = default;
        ground = 0;
        seat = "buried";
        terrain = null;
        var searchReserved = reserved.Select(r => (r.Cx, r.Cz, r.Hw + SurveyApron, r.Hl + SurveyApron)).ToList();
        var rng = RngFor(_meta.Seed ^ WorldGenerator.StableHash(_world.LocationId), "veyl_terrace_v1");
        for (int attempt = 0; attempt < 24; attempt++)
        {
            if (!TryPlaceStructureGuaranteed(structure, rng, searchReserved, wantIsland: false,
                    SeatPolicy.Factory, avoidPlayerEdits: true, out var candidate, out int surface,
                    out _, out _)) return false;
            int cx = candidate.X + structure.Width / 2, cz = candidate.Z + structure.Length / 2;
            searchReserved.Add((cx, cz, structure.Width / 2 + SurveyApron, structure.Length / 2 + SurveyApron));
            if (!TryPlanVeylTerrace(structure, version, candidate.X, candidate.Z, surface, reserved,
                    out int adjustedSurface, out terrain)) continue;
            ground = adjustedSurface - VeylSurveyGenerator.BurialDepthFor(version);
            origin = new Vector3i(candidate.X, ground, candidate.Z);
            seat = terrain!.Wet ? "wellhead_v1" : "terrace_v1";
            return true;
        }
        return false;
    }

    private bool TryPlanVeylTerrace(SettlementStructure structure, int version, int ox, int oz, int surface,
        List<(int Cx, int Cz, int Hw, int Hl)> reserved, out int adjustedSurface, out VeylTerrainPlan? plan)
    {
        adjustedSurface = surface;
        plan = null;
        var planet = _world.Planet;
        var sampled = new Dictionary<(int X, int Z), (int Ground, int Water)>();
        bool Sample(int x, int z, out (int Ground, int Water) col)
        {
            if (sampled.TryGetValue((x, z), out col)) return true;
            if (_generator.IsSurfaceLava(planet, x, z)) return false;
            int natural = _generator.SurfaceHeight(planet, x, z);
            int water = int.MinValue;
            if (_generator.TryGetWaterSurface(planet, x, z, out int top, out int seabed))
            {
                natural = seabed;
                water = top;
            }
            col = (natural, water);
            sampled.Add((x, z), col);
            return true;
        }

        int x0 = ox - SurveyApron, x1 = ox + structure.Width - 1 + SurveyApron;
        int z0 = oz - SurveyApron, z1 = oz + structure.Length - 1 + SurveyApron;
        bool wet = false;
        // Sample every column, not only the broad search's grid: a narrow river must not enter the stair.
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!Sample(x, z, out var col)) return false;
                if (col.Water != int.MinValue)
                {
                    wet = true;
                    surface = Math.Max(surface, col.Water + 2);
                }
            }

        int centre = ox + structure.Width / 2;
        int approach = 0, approachFloor = 0;
        // Join the open north mouth to natural ground (or the swim-accessible water surface). A whole
        // five-wide landing is checked; the integral ramp changes at most one floor block per metre.
        for (int distance = SurveyApron + 1; distance <= SurveyApproachLimit; distance++)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            bool valid = true;
            for (int dx = -2; dx <= 2; dx++)
            {
                if (!Sample(centre + dx, oz - distance, out var col)) { valid = false; break; }
                int top = Math.Max(col.Ground, col.Water);
                lo = Math.Min(lo, top);
                hi = Math.Max(hi, top);
            }
            if (!valid || hi - lo > 1 || Math.Abs(hi - surface) > distance - 1) continue;
            approach = distance;
            approachFloor = hi;
            break;
        }
        if (approach == 0) return false;
        z0 = oz - approach;
        int hw = (x1 - x0 + 2) / 2, hl = (z1 - z0 + 2) / 2;
        int cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        if (reserved.Any(r => Math.Abs(WorldConstants.WrapDeltaX(cx - r.Cx, _world.Circumference)) < hw + r.Hw + 2
            && Math.Abs(WorldConstants.WrapDeltaZ(cz - r.Cz, _world.Circumference)) < hl + r.Hl + 2)) return false;

        var columns = new List<VeylTerrainColumn>();
        int minY = int.MaxValue, maxY = int.MinValue, operations = 0;
        bool AddColumn(int x, int z, int floor, int shape = 0)
        {
            if (!Sample(x, z, out var col)) return false;
            wet |= col.Water != int.MinValue;
            if (Math.Abs(floor - col.Ground) > SurveyReliefLimit) return false;
            int supportTop = Math.Min(col.Ground, floor - 1);
            int support = supportTop;
            // SurfaceHeight may land on a cave aperture or decorative non-solid cell. Observe real
            // generated blocks and stop at a solid support, with an additional eight-block bound.
            while (support >= supportTop - 8 && _content.BlockById(_world.GetBlock(new Vector3i(x, support, z)))?.Solid != true)
                support--;
            if (support < supportTop - 8 || floor - support > SurveyReliefLimit) return false;
            int clear = Math.Max(floor + 3, Math.Max(col.Ground + 8, col.Water == int.MinValue ? col.Ground : col.Water + 1));
            int bottom = Math.Min(support, floor);
            operations += clear - bottom;
            if (operations > SurveyTerrainOperationLimit) return false;
            minY = Math.Min(minY, bottom);
            maxY = Math.Max(maxY, clear);
            columns.Add(new VeylTerrainColumn(x, z, support, floor, clear, shape));
            return true;
        }
        for (int x = x0; x <= x1; x++)
            for (int z = oz - SurveyApron; z <= z1; z++)
            {
                int ring = Math.Max(Math.Max(ox - x, x - (ox + structure.Width - 1)),
                    Math.Max(oz - z, z - (oz + structure.Length - 1)));
                int floor = surface - Math.Max(0, ring);
                if (z < oz && Math.Abs(x - centre) <= 2)
                    floor = surface + (approachFloor - surface) * (oz - z) / approach;
                int shape = z < oz && Math.Abs(x - centre) <= 2
                    ? VeylApproachShape(surface, approachFloor, oz - z, approach) : 0;
                if (!AddColumn(x, z, floor, shape)) return false;
            }
        for (int distance = SurveyApron + 1; distance <= approach; distance++)
            for (int dx = -2; dx <= 2; dx++)
                if (!AddColumn(centre + dx, oz - distance,
                        surface + (approachFloor - surface) * distance / approach,
                        VeylApproachShape(surface, approachFloor, distance, approach))) return false;

        // The chamber digs below the terrace and the landmark rises above it. Include both in the
        // edit check, plus the complete actual cut/fill AABB (including the approach and seabed).
        int ground = surface - VeylSurveyGenerator.BurialDepthFor(version);
        minY = Math.Min(minY, ground - MonumentPlinthDepth);
        maxY = Math.Max(maxY, ground + structure.Height);
        var min = new Vector3i(x0 - 2, minY - 2, z0 - 2);
        var max = new Vector3i(x1 + 2, maxY + 2, z1 + 2);
        if (VeylTerrainHasPlayerEdits(min, max)) return false;
        adjustedSurface = surface;
        plan = new VeylTerrainPlan(min, max, wet, columns);
        return true;
    }

    private static int VeylApproachShape(int surface, int end, int distance, int length)
    {
        int Floor(int n) => surface + (end - surface) * n / length;
        int floor = Floor(distance);
        // Stairs yaw 0 rise toward +Z; yaw 2 rise toward -Z (the authored descending vault stair).
        // Shape the HIGHER row of each transition, keeping both exterior edge rises at half a metre.
        if (distance < length && Floor(distance + 1) < floor) return ShapeCode.Pack(BlockShape.Stairs, 0);
        if (distance > 0 && Floor(distance - 1) < floor) return ShapeCode.Pack(BlockShape.Stairs, 2);
        return 0;
    }

    private bool VeylTerrainHasPlayerEdits(Vector3i min, Vector3i max)
    {
        int circ = _world.Circumference;
        // Persisted cells are canonical on BOTH torus axes. Check each half when the full terrain
        // envelope crosses a seam, so an approach cannot cut through a player's wrapped construction.
        static IEnumerable<(int Min, int Max)> Ranges(int first, int last, int period, int offset)
        {
            int start = ((first - offset) % period + period) % period + offset;
            int end = start + last - first;
            int boundary = offset + period - 1;
            if (end <= boundary) yield return (start, end);
            else
            {
                yield return (start, boundary);
                yield return (offset, offset + end - boundary - 1);
            }
        }
        int lat = WorldConstants.LatitudePeriodFor(circ);
        foreach (var x in Ranges(min.X, max.X, circ, 0))
            foreach (var z in Ranges(min.Z, max.Z, lat, -lat / 2))
                if (_repo.HasPlayerBlockEdits(_world.LocationId, new Vector3i(x.Min, min.Y, z.Min),
                        new Vector3i(x.Max, max.Y, z.Max))) return true;
        return false;
    }

    private int StampVeylTerrace(VeylTerrainPlan plan)
    {
        var fill = _content.GetBlock("basalt")?.NumericId ?? _content.GetBlock("stone")!.NumericId;
        int operations = 0;
        foreach (var c in plan.Columns)
        {
            // Fill to actual observed rock/seabed, including a complete floor cap when cutting a hill.
            for (int y = Math.Min(c.SupportY + 1, c.FloorY); y <= c.FloorY; y++)
            {
                _world.SetBlock(new Vector3i(c.X, y, c.Z), fill, shape: y == c.FloorY ? c.Shape : 0);
                operations++;
            }
            for (int y = c.FloorY + 1; y <= c.ClearY; y++)
            {
                var pos = new Vector3i(c.X, y, c.Z);
                if (_world.GetBlock(pos).IsAir) continue;
                _world.SetBlock(pos, BlockId.Air);
                operations++;
            }
        }
        return operations;
    }
}
