// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    public sealed partial class SurveyJourneyProbe
    {
        // Discover the entrance from replicated shaped cells around the observed surface inscription.
        // No world-generation class, placement origin, saved position or hidden server state is consulted.
        private bool FindEntryStair(out Vector3Int cell)
        {
            cell = default;
            float best = float.PositiveInfinity;
            int floor = Mathf.FloorToInt(_player.transform.position.y);
            for (int x = Mathf.FloorToInt(_rune.x) - 10; x <= Mathf.FloorToInt(_rune.x) + 10; x++)
            for (int z = Mathf.FloorToInt(_rune.z) - 10; z <= Mathf.FloorToInt(_rune.z) + 10; z++)
            for (int y = floor - 2; y <= floor + 1; y++)
            {
                var candidate = new Vector3Int(x, y, z);
                if (!IsStair(candidate) || !StandOn(candidate, out var feet)) continue;
                // Enter at the upper end, not through a trench wall half-way down the stair.
                if (Mathf.Abs(feet.y - _player.transform.position.y) > 1.5f) continue;
                float score = HorizontalDistance(feet, _player.transform.position) + 3f * Mathf.Abs(feet.y - _player.transform.position.y);
                if (score >= best) continue;
                best = score; cell = candidate;
            }
            return !float.IsPositiveInfinity(best);
        }

        private bool IsStair(Vector3Int p) => _game.World.TryGetBlock(p.x, p.y, p.z, out var b) && !b.IsAir
            && ShapeCode.ShapeOf(_game.World.GetShape(p.x, p.y, p.z)) == (int)BlockShape.Stairs;

        private bool StandOn(Vector3Int cell, out Vector3 feet)
            => JourneyWalkPlanner.TryFloor(_player, _capsule, (Vector3)cell + new Vector3(0.5f, 1f, 0.5f), out feet)
                && feet.y >= cell.y && feet.y <= cell.y + 1.1f;

        private bool FindLowerStair(Vector3Int current, out Vector3Int cell)
        {
            cell = default;
            float best = float.PositiveInfinity;
            var poi = Poi("veyl_excavate");
            if (poi == null) return false;
            Vector3 destination = _game.ScenePos(poi.X, current.y, poi.Z);
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0) continue;
                var candidate = current + new Vector3Int(x, -1, z);
                if (!IsStair(candidate) || !StandOn(candidate, out var feet)) continue;
                float score = HorizontalDistance(feet, destination) + (x != 0 && z != 0 ? 0.3f : 0f);
                if (score >= best) continue;
                best = score; cell = candidate;
            }
            return !float.IsPositiveInfinity(best);
        }

        private bool FindCoreRune(out Vector3Int cell)
        {
            cell = default;
            var poi = Poi("veyl_excavate");
            if (poi == null) return false;
            int x = Mathf.FloorToInt(_game.SceneX(poi.X)), z = Mathf.FloorToInt(_game.SceneZ(poi.Z));
            int floor = Mathf.FloorToInt(_player.transform.position.y);
            // Only the current walking deck: roof decorations must not become the excavation objective.
            for (int y = floor - 2; y <= floor + 2; y++)
            {
                if (!_game.World.TryGetBlock(x, y, z, out var id) || _game.Content.BlockById(id)?.Key != "rune_stone") continue;
                cell = new Vector3Int(x, y, z); return true;
            }
            return false;
        }

        /// <summary>Read-only prediction of the ordinary controller's voxel ray. It guards against
        /// clicking an unintended block; the controller still chooses and sends the actual intent.</summary>
        private bool AimCell(out Vector3Int hit, out Vector3Int previous)
        {
            hit = previous = default;
            Vector3 origin = _player.Camera.transform.position, direction = _player.Camera.transform.forward;
            var cell = Vector3Int.FloorToInt(origin);
            previous = cell;
            int sx = direction.x >= 0 ? 1 : -1, sy = direction.y >= 0 ? 1 : -1, sz = direction.z >= 0 ? 1 : -1;
            float ix = Mathf.Abs(direction.x) > 1e-6f ? 1 / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float iy = Mathf.Abs(direction.y) > 1e-6f ? 1 / Mathf.Abs(direction.y) : float.PositiveInfinity;
            float iz = Mathf.Abs(direction.z) > 1e-6f ? 1 / Mathf.Abs(direction.z) : float.PositiveInfinity;
            float tx = float.IsInfinity(ix) ? ix : (direction.x > 0 ? cell.x + 1 - origin.x : origin.x - cell.x) * ix;
            float ty = float.IsInfinity(iy) ? iy : (direction.y > 0 ? cell.y + 1 - origin.y : origin.y - cell.y) * iy;
            float tz = float.IsInfinity(iz) ? iz : (direction.z > 0 ? cell.z + 1 - origin.z : origin.z - cell.z) * iz;
            float distance = 0;
            for (int i = 0; i < 80 && distance <= _player.Reach; i++)
            {
                if (!_game.World.TryGetBlock(cell.x, cell.y, cell.z, out var block)) return false;
                var definition = _game.Content.BlockById(block);
                if (!block.IsAir && definition?.Key != "water" && definition?.Key != "lava")
                { hit = cell; return true; }
                // A ship intercepting this ray must never be mistaken for an ordinary world edit.
                if (!_game.LandedShipBlockAt(cell.x, cell.y, cell.z, out _, out _).IsAir) return false;
                previous = cell;
                if (tx <= ty && tx <= tz) { cell.x += sx; distance = tx; tx += ix; }
                else if (ty <= tz) { cell.y += sy; distance = ty; ty += iy; }
                else { cell.z += sz; distance = tz; tz += iz; }
            }
            return false;
        }
    }
}
