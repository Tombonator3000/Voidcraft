// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Bounded observed-physics navigation for the explicit journey harness. Searches are
    /// advanced in small batches; every returned waypoint still needs ordinary controller locomotion.</summary>
    internal static class JourneyWalkPlanner
    {
        private const float Cell = 0.75f;
        // The Veyl vault deliberately includes a broken bridge and a side return. Keep the search
        // bounded, but allow a short return waypoint to route around a local terrain/mesh pocket too;
        // distance alone is not evidence that the direct corridor is the only observed option.
        private const int LocalRadius = 24;  // 18 m: ordinary step/door navigation plus bounded local detours
        private const int DetourRadius = 48; // 36 m: controlled detour around the broken bridge
        private const int LocalNodeBudget = 4096;
        private const int DetourNodeBudget = 4096;
        private static readonly RaycastHit[] FloorHits = new RaycastHit[24];
        private static readonly RaycastHit[] SweepHits = new RaycastHit[24];
        private static readonly Collider[] Overlaps = new Collider[32];
        private static readonly Vector2[] FloorOffsets =
        {
            Vector2.zero, Vector2.left, Vector2.right, Vector2.up, Vector2.down,
            new(-0.7071068f, -0.7071068f), new(0.7071068f, -0.7071068f),
            new(-0.7071068f, 0.7071068f), new(0.7071068f, 0.7071068f),
        };

        internal static bool Arrived(Vector3 position, Vector3 goal, float reach, bool matchHeight)
            => new Vector2(position.x - goal.x, position.z - goal.z).sqrMagnitude <= reach * reach
                && (!matchHeight || Mathf.Abs(position.y - goal.y) < 1.25f);

        [Serializable]
        internal sealed class Rejection
        {
            public string reason, collider;
            public Vector3 position, candidatePosition, colliderMin, colliderMax;
            public bool hasCandidate, hasColliderBounds;
            public string colliderParent;
        }

        [Serializable]
        internal sealed class Report
        {
            public string status = "searching";
            public int visitedNodes, queuedNodes, boundaryNodes, noFloor, headroom, sweep,
                slopeOrHeight, unobserved, queryCapacity, planNumber;
            public float walkedForGoal;
            public Vector3 origin, goal, endpoint;
            public List<Rejection> examples = new();
            public void Reject(string reason, Vector3 position, Collider blocker = null, Vector3? candidate = null)
            {
                switch (reason)
                {
                    case "no_floor": noFloor++; break;
                    case "headroom": headroom++; break;
                    case "sweep": sweep++; break;
                    case "slope_or_height": slopeOrHeight++; break;
                    case "unobserved": unobserved++; break;
                    case "query_capacity": queryCapacity++; break;
                }
                if (examples.Count < 12)
                    examples.Add(new Rejection
                    {
                        reason = reason, position = position, collider = blocker == null ? string.Empty : blocker.name,
                        hasCandidate = candidate.HasValue, candidatePosition = candidate ?? position,
                        hasColliderBounds = blocker != null,
                        colliderMin = blocker != null ? blocker.bounds.min : Vector3.zero,
                        colliderMax = blocker != null ? blocker.bounds.max : Vector3.zero,
                        colliderParent = blocker != null && blocker.transform.parent != null ? blocker.transform.parent.name : string.Empty,
                    });
            }
        }

        /// <summary>One objective's finite exploration history. Only actually walked positions and
        /// selected frontiers are remembered; none of this state alters the world or player pose.</summary>
        internal sealed class Memory
        {
            private readonly HashSet<Vector2Int> _visited = new();
            private readonly HashSet<Vector2Int> _frontiers = new();
            private Vector3 _last;
            private bool _hasLast;
            public int Plans { get; private set; }
            public float Walked { get; private set; }
            public bool Exhausted => Plans >= 16 || Walked >= 256f;
            private static Vector2Int Key(Vector3 p) => new(Mathf.FloorToInt(p.x / 1.5f), Mathf.FloorToInt(p.z / 1.5f));
            public void Observe(Vector3 position)
            {
                if (_hasLast) Walked += Vector3.Distance(_last, position);
                _last = position; _hasLast = true;
                _visited.Add(Key(position));
            }
            public void BeginPlan() => Plans++;
            public bool FreshFrontier(Vector3 p)
            {
                var key = Key(p);
                for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    if (_visited.Contains(key + new Vector2Int(x, z)) || _frontiers.Contains(key + new Vector2Int(x, z))) return false;
                return true;
            }
            public void SelectFrontier(Vector3 p) => _frontiers.Add(Key(p));
        }

        private sealed class Node
        {
            public Vector2Int Key;
            public Vector3 Position;
            public float Cost, Score;
            public Node Previous;
            public bool Closed;
        }

        internal sealed class Search
        {
            private readonly PlayerController _player;
            private readonly CharacterController _capsule;
            private readonly Memory _memory;
            private readonly Vector3 _origin, _goal;
            private readonly bool _matchHeight;
            private readonly float _reach;
            private readonly int _radius;
            private readonly int _nodeBudget;
            private readonly Dictionary<Vector2Int, Node> _nodes = new();
            private readonly List<Node> _open = new();
            private readonly List<Node> _boundary = new();
            public readonly Report Diagnostics;
            public readonly List<Vector3> Route = new();
            public bool Done { get; private set; }

            public Search(PlayerController player, CharacterController capsule, Vector3 goal,
                float reach, bool matchHeight, Memory memory, int nodeBudget = -1)
            {
                _player = player; _capsule = capsule; _goal = goal; _origin = player.transform.position;
                _reach = reach; _matchHeight = matchHeight; _memory = memory;
                float horizontalGoalDistance = new Vector2(_origin.x - goal.x, _origin.z - goal.z).magnitude;
                bool needsDetour = horizontalGoalDistance > 12f;
                _radius = needsDetour ? DetourRadius : LocalRadius;
                _nodeBudget = nodeBudget > 0 ? nodeBudget : needsDetour ? DetourNodeBudget : LocalNodeBudget;
                Diagnostics = new Report { origin = _origin, goal = goal, walkedForGoal = memory.Walked, planNumber = memory.Plans + 1 };
                if (memory.Exhausted) { Complete("goal_exploration_budget_exhausted"); return; }
                memory.BeginPlan();
                var start = new Node { Position = _origin, Score = Distance(_origin, goal) };
                _nodes.Add(Vector2Int.zero, start); _open.Add(start);
            }

            private float Distance(Vector3 a, Vector3 b) => _matchHeight
                ? Vector3.Distance(a, b) : new Vector2(a.x - b.x, a.z - b.z).magnitude;

            /// <summary>At most the caller's node batch is inspected. There are no tasks/threads or
            /// background Unity physics calls, and dense queries fail conservatively instead of truncating.</summary>
            public void Advance(int nodeBatch)
            {
                int expanded = 0;
                while (!Done && _open.Count > 0 && expanded < nodeBatch && Diagnostics.visitedNodes < _nodeBudget)
                {
                    int index = 0;
                    for (int i = 1; i < _open.Count; i++) if (_open[i].Score < _open[index].Score) index = i;
                    var current = _open[index]; _open.RemoveAt(index);
                    if (current.Closed) continue;
                    current.Closed = true; expanded++; Diagnostics.visitedNodes++;
                    if (Arrived(current.Position, _goal, _reach, _matchHeight))
                    { UseRoute(current, "goal_route"); return; }
                    if (Mathf.Abs(current.Key.x) == _radius || Mathf.Abs(current.Key.y) == _radius)
                    { _boundary.Add(current); Diagnostics.boundaryNodes++; }
                    for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && z == 0) continue;
                        var key = current.Key + new Vector2Int(x, z);
                        if (Mathf.Abs(key.x) > _radius || Mathf.Abs(key.y) > _radius) continue;
                        if (_nodes.TryGetValue(key, out var existing) && existing.Closed) continue;
                        Vector3 sample = new(_origin.x + key.x * Cell, current.Position.y, _origin.z + key.y * Cell);
                        if (!TryFloor(_player, _capsule, sample, out var next, Diagnostics)
                            || !ClearStep(_player, _capsule, current.Position, next, Diagnostics)) continue;
                        float cost = current.Cost + Vector3.Distance(current.Position, next);
                        if (existing != null && existing.Cost <= cost) continue;
                        var node = existing ?? new Node { Key = key };
                        node.Position = next; node.Cost = cost; node.Score = cost + Distance(next, _goal); node.Previous = current;
                        _nodes[key] = node;
                        if (existing == null) _open.Add(node);
                    }
                }
                Diagnostics.queuedNodes = _open.Count;
                if (Done || (expanded >= nodeBatch && _open.Count > 0 && Diagnostics.visitedNodes < _nodeBudget)) return;
                // A boundary is a verified route to more observable space. It may initially lead away
                // from the objective; reject already visited/selected frontiers to avoid local oscillation.
                Node frontier = null;
                float best = float.PositiveInfinity;
                foreach (var node in _boundary)
                {
                    if (!_memory.FreshFrontier(node.Position)) continue;
                    float score = Distance(node.Position, _goal) + node.Cost * 0.15f;
                    if (score >= best) continue;
                    best = score; frontier = node;
                }
                if (frontier != null)
                {
                    _memory.SelectFrontier(frontier.Position);
                    UseRoute(frontier, Diagnostics.visitedNodes >= _nodeBudget ? "bounded_frontier_route" : "frontier_route");
                    return;
                }
                Complete(Diagnostics.visitedNodes >= _nodeBudget ? "node_budget_exhausted"
                    : Diagnostics.queryCapacity > 0 ? "physics_query_capacity_exhausted"
                    : Diagnostics.unobserved > 0 ? "unobserved_surface_boundary"
                    : "observed_geometry_unreachable");
            }

            private void UseRoute(Node end, string status)
            {
                for (var node = end; node.Previous != null; node = node.Previous) Route.Add(node.Position);
                Route.Reverse(); Diagnostics.endpoint = end.Position; Complete(status);
            }
            private void Complete(string status) { Diagnostics.status = status; Done = true; }
        }

        // Synchronous inspection wrapper used only by the small EditMode physics fixtures.
        public static List<Vector3> Plan(PlayerController player, CharacterController capsule, Vector3 goal)
        {
            var search = new Search(player, capsule, goal, Cell, true, new Memory());
            while (!search.Done) search.Advance(32);
            return search.Route;
        }

        private static bool Self(PlayerController player, Collider collider)
            => collider.transform == player.transform || collider.transform.IsChildOf(player.transform);

        private static bool Observed(PlayerController player, Vector3 min, Vector3 max, Report report)
        {
            if (player.Game == null) return true; // isolated physics-only test fixtures, never the live journey
            var world = player.Game.World;
            for (int x = Mathf.FloorToInt(min.x); x <= Mathf.FloorToInt(max.x); x++)
            for (int y = Mathf.FloorToInt(min.y); y <= Mathf.FloorToInt(max.y); y++)
            for (int z = Mathf.FloorToInt(min.z); z <= Mathf.FloorToInt(max.z); z++)
            {
                if (world != null && world.TryGetBlock(x, y, z, out _)) continue;
                report?.Reject("unobserved", new Vector3(x, y, z)); return false;
            }
            return true;
        }

        internal static bool TryFloor(PlayerController player, CharacterController capsule, Vector3 sample, out Vector3 feet, Report report = null)
        {
            feet = default;
            float radius = capsule.radius + 0.015f;
            if (!Observed(player, sample - new Vector3(radius, 2.2f, radius),
                sample + new Vector3(radius, capsule.height + 1.2f, radius), report)) return false;
            float top = float.NegativeInfinity;
            string reason = "no_floor";
            Collider blocker = null;
            Vector3? rejectedCandidate = null;
            // Keep a clear centre-floor height: a low edge beside the rounded capsule must not lift
            // a valid route into a ceiling. Only a blocked/missing centre floor needs the other eight
            // bounded footprint rays (e.g. a stair's high tread). Each height still checks the SAME
            // centred capsule; this is a clearance waypoint, not an injected resting/body pose.
            foreach (var offset in FloorOffsets)
            {
                Vector3 ray = sample + new Vector3(offset.x * capsule.radius * 0.98f, 1.2f, offset.y * capsule.radius * 0.98f);
                int count = Physics.RaycastNonAlloc(ray, Vector3.down, FloorHits, 3.5f, ~0, QueryTriggerInteraction.Ignore);
                if (count >= FloorHits.Length) { report?.Reject("query_capacity", sample); return false; }
                for (int i = 0; i < count; i++)
                {
                    var hit = FloorHits[i];
                    if (Self(player, hit.collider)) continue;
                    if (hit.normal.y < Mathf.Cos(capsule.slopeLimit * Mathf.Deg2Rad)
                        || hit.point.y > sample.y + 1.05f || hit.point.y < sample.y - 2.1f)
                    { reason = "slope_or_height"; blocker = hit.collider; continue; }
                    if (hit.point.y <= top) continue;
                    Vector3 candidate = new(sample.x, hit.point.y + 0.03f, sample.z);
                    Vector3 lower = candidate + Vector3.up * (radius + 0.025f);
                    Vector3 upper = candidate + Vector3.up * (capsule.height - radius);
                    int overlaps = Physics.OverlapCapsuleNonAlloc(lower, upper, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
                    if (overlaps >= Overlaps.Length) { report?.Reject("query_capacity", sample, candidate: candidate); return false; }
                    bool clear = true;
                    for (int j = 0; j < overlaps; j++)
                        if (!Self(player, Overlaps[j])) { clear = false; blocker = Overlaps[j]; break; }
                    if (!clear) { reason = "headroom"; rejectedCandidate = candidate; continue; }
                    top = hit.point.y; feet = candidate;
                }
                if (offset == Vector2.zero && !float.IsNegativeInfinity(top)) return true;
            }
            if (!float.IsNegativeInfinity(top)) return true;
            report?.Reject(reason, sample, blocker, rejectedCandidate); return false;
        }

        private static bool ClearStep(PlayerController player, CharacterController capsule, Vector3 from, Vector3 to, Report report)
        {
            // Walk level until clear of a deck edge, then settle; a diagonal descent cuts through its lip.
            float level = Mathf.Max(from.y, to.y) + 0.04f;
            Vector3 start = from + Vector3.up * 0.04f;
            Vector3 raised = new(from.x, level, from.z), across = new(to.x, level, to.z);
            return ClearSweep(player, capsule, start, raised, report)
                && ClearSweep(player, capsule, raised, across, report)
                && ClearSweep(player, capsule, across, to + Vector3.up * 0.04f, report);
        }

        private static bool ClearSweep(PlayerController player, CharacterController capsule, Vector3 from, Vector3 to, Report report)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.000001f) return true;
            float radius = capsule.radius + 0.015f;
            if (!Observed(player, Vector3.Min(from, to) - new Vector3(radius, 0, radius),
                Vector3.Max(from, to) + new Vector3(radius, capsule.height, radius), report)) return false;
            Vector3 lower = from + Vector3.up * radius, upper = from + Vector3.up * (capsule.height - radius);
            int count = Physics.CapsuleCastNonAlloc(lower, upper, radius, delta.normalized, SweepHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count >= SweepHits.Length) { report?.Reject("query_capacity", to); return false; }
            for (int i = 0; i < count; i++)
                if (!Self(player, SweepHits[i].collider)) { report?.Reject("sweep", to, SweepHits[i].collider); return false; }
            return true;
        }
    }
}
