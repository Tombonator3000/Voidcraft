// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A bounded local waypoint suggestion based on currently loaded physics. It never changes
    /// transforms or colliders: the ordinary controller must still traverse every suggested step.</summary>
    internal static class JourneyWalkPlanner
    {
        private const float Cell = 0.75f;
        private const int Radius = 9;
        private const int NodeBudget = 192;
        private sealed class Node
        {
            public Vector2Int Key;
            public Vector3 Position;
            public float Cost, Score;
            public Node Previous;
            public bool Closed;
        }

        public static List<Vector3> Plan(PlayerController player, CharacterController capsule, Vector3 goal)
        {
            Vector3 origin = player.transform.position;
            var start = new Node { Position = origin, Score = Distance(origin, goal) };
            var nodes = new Dictionary<Vector2Int, Node> { [Vector2Int.zero] = start };
            var open = new List<Node> { start };
            Node best = start;
            int visits = 0;
            while (open.Count > 0 && visits++ < NodeBudget)
            {
                int index = 0;
                for (int i = 1; i < open.Count; i++) if (open[i].Score < open[index].Score) index = i;
                var current = open[index];
                open.RemoveAt(index);
                if (current.Closed) continue;
                current.Closed = true;
                if (Distance(current.Position, goal) < Distance(best.Position, goal)) best = current;
                if (Distance(current.Position, goal) < Cell) break;
                for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && z == 0) continue;
                    var key = current.Key + new Vector2Int(x, z);
                    if (Mathf.Abs(key.x) > Radius || Mathf.Abs(key.y) > Radius) continue;
                    if (nodes.TryGetValue(key, out var existing) && existing.Closed) continue;
                    Vector3 sample = new(origin.x + key.x * Cell, current.Position.y, origin.z + key.y * Cell);
                    if (!TryFloor(player, capsule, sample, out Vector3 next) || !ClearStep(player, capsule, current.Position, next)) continue;
                    float cost = current.Cost + Vector3.Distance(current.Position, next);
                    if (existing != null && existing.Cost <= cost) continue;
                    var node = existing ?? new Node { Key = key };
                    node.Position = next;
                    node.Cost = cost;
                    node.Score = cost + Distance(next, goal);
                    node.Previous = current;
                    nodes[key] = node;
                    if (existing == null) open.Add(node);
                }
            }
            var route = new List<Vector3>();
            if (Distance(best.Position, goal) >= Distance(start.Position, goal) - 0.3f) return route;
            for (var node = best; node.Previous != null; node = node.Previous) route.Add(node.Position);
            route.Reverse();
            return route;
        }

        private static float Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);
        private static bool Self(PlayerController player, Collider collider)
            => collider.transform == player.transform || collider.transform.IsChildOf(player.transform);

        internal static bool TryFloor(PlayerController player, CharacterController capsule, Vector3 sample, out Vector3 feet)
        {
            feet = default;
            var hits = Physics.RaycastAll(sample + Vector3.up * 1.2f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
            float top = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (Self(player, hit.collider) || hit.normal.y < Mathf.Cos(capsule.slopeLimit * Mathf.Deg2Rad)
                    || hit.point.y > sample.y + 1.05f || hit.point.y < sample.y - 2.1f || hit.point.y <= top) continue;
                Vector3 candidate = new(sample.x, hit.point.y + 0.03f, sample.z);
                float radius = capsule.radius + 0.015f;
                Vector3 lower = candidate + Vector3.up * (radius + 0.025f);
                Vector3 upper = candidate + Vector3.up * (capsule.height - radius);
                bool clear = true;
                foreach (var other in Physics.OverlapCapsule(lower, upper, radius, ~0, QueryTriggerInteraction.Ignore))
                    if (!Self(player, other)) { clear = false; break; }
                if (!clear) continue;
                top = hit.point.y;
                feet = candidate;
            }
            return !float.IsNegativeInfinity(top);
        }

        private static bool ClearStep(PlayerController player, CharacterController capsule, Vector3 from, Vector3 to)
        {
            // Walking off a deck stays level until the capsule clears its edge, then gravity settles
            // onto the lower floor. A diagonal downward sweep cuts through that edge and rejects an
            // ordinary hatch exit. Probe the same raised, horizontal, then settling route instead.
            float level = Mathf.Max(from.y, to.y) + 0.04f;
            Vector3 start = from + Vector3.up * 0.04f;
            Vector3 raised = new(from.x, level, from.z);
            Vector3 across = new(to.x, level, to.z);
            return ClearSweep(player, capsule, start, raised)
                && ClearSweep(player, capsule, raised, across)
                && ClearSweep(player, capsule, across, to + Vector3.up * 0.04f);
        }

        private static bool ClearSweep(PlayerController player, CharacterController capsule, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.000001f) return true;
            float radius = capsule.radius + 0.015f;
            Vector3 lower = from + Vector3.up * radius;
            Vector3 upper = from + Vector3.up * (capsule.height - radius);
            foreach (var hit in Physics.CapsuleCastAll(lower, upper, radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!Self(player, hit.collider)) return false;
            return true;
        }
    }
}
