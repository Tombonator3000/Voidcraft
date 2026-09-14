// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Object = UnityEngine.Object;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class JourneyWalkPlannerEditModeTests
    {
        private GameObject _root;
        private PlayerController _player;
        private CharacterController _capsule;
        private readonly List<Mesh> _meshes = new();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Journey route physics");
            var body = new GameObject("Ordinary capsule");
            body.transform.SetParent(_root.transform);
            _capsule = body.AddComponent<CharacterController>();
            _capsule.height = 1.8f;
            _capsule.radius = 0.3f;
            _capsule.center = Vector3.up * 0.9f;
            _capsule.slopeLimit = 45f;
            _player = body.AddComponent<PlayerController>();
            _player.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (var mesh in _meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            _meshes.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void OpenHatchHasARealRouteOffTheOneMeterDeck()
        {
            DeckAndGround();
            _player.transform.position = new Vector3(0f, 1.03f, -0.3f);
            Physics.SyncTransforms();
            var route = Plan(new Vector3(0f, 0.03f, 3f));
            Assert.That(route.Count, Is.GreaterThan(0));
            Assert.That(route[route.Count - 1].z, Is.GreaterThan(1f), "The route must actually leave the deck.");
            Assert.That(route[route.Count - 1].y, Is.EqualTo(0.03f).Within(0.02f));
            Assert.That(_player.transform.position, Is.EqualTo(new Vector3(0f, 1.03f, -0.3f)), "Planning cannot move the player.");
        }

        [Test]
        public void ClosedHatchCannotBecomeAWaypointThroughTheWall()
        {
            DeckAndGround();
            Box("Closed hatch wall", new Vector3(0f, 2.5f, 0f), new Vector3(20f, 3f, 0.2f));
            _player.transform.position = new Vector3(0f, 1.03f, -0.7f);
            Physics.SyncTransforms();
            foreach (var point in Plan(new Vector3(0f, 0.03f, 3f)))
                Assert.That(point.z, Is.LessThan(-0.39f), "A capsule cannot cross a closed hatch.");
        }

        [Test]
        public void UnloadedVoidAndUnsafeDropDoNotProduceAWalkOffRoute()
        {
            Box("Deck", new Vector3(0f, 0.5f, -2f), new Vector3(20f, 1f, 4f));
            _player.transform.position = new Vector3(0f, 1.03f, -0.3f);
            Physics.SyncTransforms();
            Assert.That(Plan(new Vector3(0f, -3f, 3f)), Is.Empty);
            Box("Distant floor", new Vector3(0f, -3.5f, 3f), new Vector3(20f, 1f, 6f));
            Physics.SyncTransforms();
            Assert.That(Plan(new Vector3(0f, -3f, 3f)), Is.Empty, "The planner's bounded drop limit still applies.");
        }

        [Test]
        public void ReachableDetourCanStartAwayFromTheGoalAndLeaveTheOldLocalWindow()
        {
            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(50f, 1f, 50f));
            // A U opens behind the player, more than 6.75 m away. Its front faces the goal.
            Box("Front wall", new Vector3(0f, 2f, 2f), new Vector3(4.2f, 4f, 0.2f));
            Box("Left wall", new Vector3(-2f, 2f, -4f), new Vector3(0.2f, 4f, 12f));
            Box("Right wall", new Vector3(2f, 2f, -4f), new Vector3(0.2f, 4f, 12f));
            _player.transform.position = new Vector3(0f, 0.03f, 0f);
            var goal = new Vector3(0f, 0.03f, 5f);
            Physics.SyncTransforms();
            var result = Search(goal);
            Assert.AreEqual("goal_route", Status(result.Report));
            Assert.That(result.Route.Count, Is.GreaterThan(0));
            Assert.That(Vector3.Distance(result.Route[0], goal), Is.GreaterThan(Vector3.Distance(_player.transform.position, goal)),
                "A valid escape must be allowed to move away before circling the wall.");
            Assert.IsTrue(result.Route.Exists(p => p.z < -10f), "The route must actually go around the far end of the U.");
            Assert.That(Vector3.Distance(result.Route[result.Route.Count - 1], goal), Is.LessThan(0.8f));
            Assert.AreEqual(new Vector3(0f, 0.03f, 0f), _player.transform.position, "The planner still cannot move the player.");
        }

        [Test]
        public void DistantGoalProducesFreshObservedFrontiersRatherThanOscillatingBack()
        {
            Box("Ground", new Vector3(30f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
            _player.transform.position = new Vector3(0f, 0.03f, 0f);
            Physics.SyncTransforms();
            var memory = NewMemory();
            Observe(memory, _player.transform.position);
            var goal = new Vector3(60f, 0.03f, 0f);
            var first = Search(goal, memory);
            Assert.That(Status(first.Report), Does.Contain("frontier_route"));
            Vector3 end = first.Route[first.Route.Count - 1];
            Assert.That(end.x, Is.GreaterThan(6.75f));
            // Arrange the next planning query at the verified endpoint. This fixture checks planning,
            // not runtime locomotion; the production driver never writes this pose.
            foreach (var point in first.Route) Observe(memory, point);
            _player.transform.position = end;
            Physics.SyncTransforms();
            var second = Search(goal, memory);
            Assert.That(second.Route.Count, Is.GreaterThan(0));
            Assert.That(second.Route[second.Route.Count - 1].x, Is.GreaterThan(end.x + 6.75f));
        }

        [Test]
        public void AWallBeyondTheExpandedWindowRequiresAnAwayFrontierBeforeTurningBack()
        {
            Box("Ground", new Vector3(0f, -0.5f, -10f), new Vector3(100f, 1f, 100f));
            Box("Front wall", new Vector3(0f, 2f, 2f), new Vector3(4.2f, 4f, 0.2f));
            Box("Left long wall", new Vector3(-2f, 2f, -11f), new Vector3(0.2f, 4f, 26f));
            Box("Right long wall", new Vector3(2f, 2f, -11f), new Vector3(0.2f, 4f, 26f));
            _player.transform.position = new Vector3(0f, 0.03f, 0f);
            var goal = new Vector3(0f, 0.03f, 5f);
            var memory = NewMemory();
            Observe(memory, _player.transform.position);
            Physics.SyncTransforms();
            var first = Search(goal, memory);
            Assert.That(Status(first.Report), Does.Contain("frontier_route"));
            Vector3 firstEnd = first.Route[first.Route.Count - 1];
            Assert.That(firstEnd.z, Is.LessThan(-17f), "The only observed frontier initially leads away from the goal.");
            foreach (var point in first.Route) Observe(memory, point);
            _player.transform.position = firstEnd; // arrange the next query, not runtime movement
            Physics.SyncTransforms();
            var second = Search(goal, memory);
            Assert.That(second.Route.Count, Is.GreaterThan(0));
            Assert.IsTrue(second.Route.Exists(p => p.z < -24f), "The second window must discover the real end of the wall.");
            Assert.That(second.Route[second.Route.Count - 1].z, Is.GreaterThan(firstEnd.z + 6.75f),
                "After escaping, frontier memory must let the route turn back toward the objective outside the U.");
        }

        [Test]
        public void SealedGeometryTerminatesWithCappedActualBlockerEvidence()
        {
            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            Box("Front wall", new Vector3(0f, 2f, 1f), new Vector3(2.2f, 4f, 0.2f));
            Box("Back wall", new Vector3(0f, 2f, -1f), new Vector3(2.2f, 4f, 0.2f));
            Box("Left wall", new Vector3(-1f, 2f, 0f), new Vector3(0.2f, 4f, 2.2f));
            Box("Right wall", new Vector3(1f, 2f, 0f), new Vector3(0.2f, 4f, 2.2f));
            _player.transform.position = new Vector3(0f, 0.03f, 0f);
            Physics.SyncTransforms();
            var result = Search(new Vector3(0f, 0.03f, 8f));
            Assert.IsEmpty(result.Route);
            Assert.AreEqual("observed_geometry_unreachable", Status(result.Report));
            Assert.That((int)result.Report.GetType().GetField("headroom").GetValue(result.Report), Is.GreaterThan(0));
            var examples = (System.Collections.IList)result.Report.GetType().GetField("examples").GetValue(result.Report);
            Assert.That(examples.Count, Is.InRange(1, 12));
            Assert.That((string)examples[0].GetType().GetField("collider").GetValue(examples[0]), Does.Contain("wall"));
        }

        [Test]
        public void ExhaustedSearchBudgetIsDistinctFromUnreachableGeometry()
        {
            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(50f, 1f, 50f));
            _player.transform.position = new Vector3(0f, 0.03f, 0f);
            Physics.SyncTransforms();
            var result = Search(new Vector3(10f, 0.03f, 0f), nodeBudget: 1);
            Assert.IsEmpty(result.Route);
            Assert.AreEqual("node_budget_exhausted", Status(result.Report));
        }

        [Test]
        public void APhysicsFloorBeyondLoadedCellsCannotBeUsedAsKnownEmptyTerrain()
        {
            Box("Ground", new Vector3(20f, 7.5f, 8f), new Vector3(70f, 1f, 50f));
            var game = _root.AddComponent<GameBootstrap>();
            var world = new ClientWorld();
            world.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[WorldConstants.BlocksPerChunk]);
            typeof(GameBootstrap).GetProperty("World").SetValue(game, world);
            _player.Game = game;
            float edge = WorldConstants.ChunkSize;
            _player.transform.position = new Vector3(edge - 1.5f, 8.03f, 8f);
            Physics.SyncTransforms();
            var result = Search(new Vector3(edge + 8f, 8.03f, 8f));
            Assert.That((int)result.Report.GetType().GetField("unobserved").GetValue(result.Report), Is.GreaterThan(0));
            Assert.That(Status(result.Report), Is.Not.EqualTo("goal_route"));
            Assert.IsTrue(result.Route.TrueForAll(p => p.x + _capsule.radius < edge),
                "A collider can outlive streamed cells; its missing ceiling/side cells are not known air.");
        }

        [Test]
        public void RepeatedWholeGoalSearchesHaveAFiniteLimitEvenWithoutMovement()
        {
            var memory = NewMemory();
            object search = null;
            int plans = 0;
            while (plans++ < 64)
            {
                search = NewSearch(new Vector3(50f, 0f, 0f), memory, 1);
                if ((bool)search.GetType().GetProperty("Done").GetValue(search)) break;
            }
            Assert.That(plans, Is.LessThan(64), "Repeated queries must not create an endless exploration loop.");
            Assert.AreEqual("goal_exploration_budget_exhausted", Status(search.GetType().GetField("Diagnostics").GetValue(search)));
        }

        [Test]
        public void AuthoredStarterStairReproducesCentreRayFalseHeadroomAndFindsFootprintClearance()
        {
            BuildObservedStarterHatch();
            var sample = new Vector3(0.51493406f, 59.03f, -5.8108606f);
            Assert.IsTrue(Physics.Raycast(sample + Vector3.up * 1.2f, Vector3.down, out var hit, 3.5f,
                ~0, QueryTriggerInteraction.Ignore));
            Assert.That(hit.point.y, Is.EqualTo(58.5f).Within(0.005f), "The recorded sample hits the lower authored tread.");
            float radius = _capsule.radius + 0.015f;
            Vector3 centreOnly = new(sample.x, hit.point.y + 0.03f, sample.z);
            Assert.IsTrue(Physics.OverlapCapsule(centreOnly + Vector3.up * (radius + 0.025f),
                centreOnly + Vector3.up * (_capsule.height - radius), radius, ~0, QueryTriggerInteraction.Ignore)
                .Any(c => c.name == "ShipChunk 0,0,-1"), "The earlier single-ray query must reproduce the actual stair collision.");
            var planner = typeof(PlayerController).Assembly.GetType("BlocksBeyondTheStars.Client.JourneyWalkPlanner");
            object[] args = { _player, _capsule, sample, Vector3.zero, null };
            Assert.IsTrue((bool)planner.GetMethod("TryFloor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args));
            var clearance = (Vector3)args[3];
            Assert.That(clearance.y, Is.EqualTo(59.03f).Within(0.005f), "The observed higher tread yields a clear centred waypoint; the real rounded capsule may settle lower.");
            Assert.That(_player.transform.position, Is.EqualTo(new Vector3(0.51493406f, 59.03f, -2.8108604f)));
        }

        [Test]
        public void ActualStarterCapsuleCanWalkThroughTheRecordedOpenHatchWithoutJumping()
        {
            BuildObservedStarterHatch();
            // Fixture locomotion uses Unity's real ordinary capsule; no translation after the initial
            // recorded-pose arrangement. This isolates physical accessibility from harness planning.
            for (int i = 0; i < 150; i++) _capsule.Move(new Vector3(0f, -0.04f, -0.035f));
            Assert.That(_player.transform.position.z, Is.LessThan(-6.75f));
            Assert.That(_player.transform.position.y, Is.EqualTo(58.03f).Within(0.12f));
        }

        [Test]
        public void LoadedStarterExitUsesTheDriversHorizontalAndVerticalArrivalPredicate()
        {
            BuildObservedStarterHatch();
            var start = _player.transform.position;
            var goal = new Vector3(0f, 59f, -7f); // actual journey target retains the one-metre-higher cabin floor
            var result = Search(goal);
            Assert.AreEqual("goal_route", Status(result.Report));
            Assert.That(result.Route.Count, Is.GreaterThan(0));
            var end = result.Route[result.Route.Count - 1];
            Assert.That(end.z, Is.LessThan(-6.35f));
            Assert.That(end.y, Is.EqualTo(58.03f).Within(0.02f));
            Assert.That(Vector3.Distance(end, goal), Is.GreaterThan(0.7f), "The old spherical predicate incorrectly rejected this ordinary landing.");
            Assert.IsTrue(Arrived(end, goal, 0.65f, true));
            Assert.That(_player.transform.position, Is.EqualTo(start), "Planning never moves the real player body.");
        }

        [Test]
        public void ClearCentreFloorBesideAQuarterPanelDoesNotRiseIntoTheLowCeiling()
        {
            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Box("Ceiling underside 2.10", new Vector3(0f, 2.15f, 0f), new Vector3(20f, 0.1f, 20f));
            Box("Left corridor wall", new Vector3(-1f, 1f, 0f), new Vector3(1f, 2f, 20f));
            Box("Right corridor wall", new Vector3(2f, 1f, 0f), new Vector3(1f, 2f, 20f));
            var content = ContentLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "data"));
            var block = content.GetBlock("steel_floor").NumericId;
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            int panel = ShapeCode.Pack(BlockShape.Panel, 0);
            for (int z = 0; z < 6; z++) { chunk.Set(0, 0, z, block); chunk.SetShape(0, 0, z, panel); }
            var (render, collider) = ChunkMesher.Build(chunk, content,
                (x, y, z) => x == 0 && y == 0 && z >= 0 && z < 6 ? block : BlockId.Air,
                worldShape: (x, y, z) => x == 0 && y == 0 && z >= 0 && z < 6 ? panel : 0);
            _meshes.Add(render); _meshes.Add(collider);
            var edge = new GameObject("Authored quarter-height Panel edge");
            edge.transform.SetParent(_root.transform);
            edge.transform.position = new Vector3(0.34f, 0f, -1f);
            edge.AddComponent<MeshCollider>().sharedMesh = collider;
            _capsule.radius = 0.35f; _capsule.skinWidth = 0.03f; _capsule.stepOffset = 0.6f;
            var start = new Vector3(0f, 0.03f, -0.75f);
            _player.transform.position = start;
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(new Vector3(0.343f, 1.2f, 0f), Vector3.down, out var edgeHit, 2f,
                ~0, QueryTriggerInteraction.Ignore));
            Assert.That(edgeHit.point.y, Is.EqualTo(0.25f).Within(0.005f), "An offset ray really sees the side panel.");
            var planner = typeof(PlayerController).Assembly.GetType("BlocksBeyondTheStars.Client.JourneyWalkPlanner");
            object[] args = { _player, _capsule, new Vector3(0f, 0.03f, 0f), Vector3.zero, null };
            Assert.IsTrue((bool)planner.GetMethod("TryFloor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args));
            Assert.That(((Vector3)args[3]).y, Is.EqualTo(0.03f).Within(0.005f), "A valid centre floor must win over the optional raised side edge.");
            var step = planner.GetMethod("ClearStep", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsFalse((bool)step.Invoke(null, new object[] { _player, _capsule, start, new Vector3(0f, 0.28f, 0f), null }),
                "The previous highest-offset choice incorrectly swept into this ceiling.");
            Assert.IsTrue((bool)step.Invoke(null, new object[] { _player, _capsule, start, (Vector3)args[3], null }));
            var result = Search(new Vector3(0f, 0.03f, 3f));
            Assert.AreEqual("goal_route", Status(result.Report));
            Assert.That(result.Route.TrueForAll(p => Mathf.Abs(p.y - 0.03f) < 0.01f), Is.True);
            Assert.AreEqual(start, _player.transform.position, "Planning cannot move the body.");
            for (int i = 0; i < 70; i++) _capsule.Move(new Vector3(0f, -0.04f, 0.05f));
            Assert.That(_player.transform.position.z, Is.GreaterThan(2.5f), "The ordinary capsule must really fit beside the panel and below the ceiling.");
        }

        [Test]
        public void SharedArrivalStillRejectsWrongStoreyAndHorizontalMiss()
        {
            var goal = new Vector3(3f, 9f, -4f);
            Assert.IsTrue(Arrived(goal + new Vector3(0.2f, -1f, 0.2f), goal, 0.7f, true));
            Assert.IsFalse(Arrived(goal + Vector3.up * 1.25f, goal, 0.7f, true));
            Assert.IsFalse(Arrived(goal + Vector3.forward * 0.71f, goal, 0.7f, true));
            Assert.IsTrue(Arrived(goal + Vector3.up * 4f, goal, 0.7f, false));
        }

        private static bool Arrived(Vector3 position, Vector3 goal, float reach, bool matchHeight)
            => (bool)typeof(PlayerController).Assembly.GetType("BlocksBeyondTheStars.Client.JourneyWalkPlanner")
                .GetMethod("Arrived", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { position, goal, reach, matchHeight });

        private void BuildObservedStarterHatch()
        {
            var content = ContentLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "data"));
            var layout = content.GetShipLayout("ship_starter_home");
            Assert.NotNull(layout);
            Assert.AreEqual((6, 4, 11), (layout.Width, layout.Height, layout.Length));
            var cells = new Dictionary<Vector3i, BlockId>();
            var shapes = new Dictionary<Vector3i, int>();
            foreach (var cell in layout.Cells)
            {
                if (cell.Id is "hatch" or "door_slide" or "door_hinge" or "door_energy") continue;
                // Station marker materials differ visually; they are the same occupied full cube for
                // collision. Element mapping follows the served starter's ordinary structure path.
                string key = cell.Kind == "station" ? "iron_wall" : cell.Id switch
                {
                    "engine" => "carbon", "light" or "headlight" => "light_white", _ => cell.Id,
                };
                var block = content.GetBlock(key) ?? content.GetBlock("iron_wall");
                var point = new Vector3i(cell.X, cell.Y, cell.Z);
                cells[point] = block.NumericId;
                if (cell.Shape != 0) shapes[point] = cell.Shape;
            }
            Assert.AreEqual(ShapeCode.Pack(BlockShape.Stairs, 0), shapes[new Vector3i(2, 0, -1)]);
            Assert.AreEqual(ShapeCode.Pack(BlockShape.Stairs, 0), shapes[new Vector3i(3, 0, -1)]);
            BlockId Cell(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var block) ? block : BlockId.Air;
            int Shape(int x, int y, int z) => shapes.TryGetValue(new Vector3i(x, y, z), out int value) ? value : 0;
            var ship = new GameObject("LandedShip Pilot");
            ship.transform.SetParent(_root.transform);
            ship.transform.position = new Vector3(-3f, 58f, -5f); // recorded server origin, unrotated local chunks
            foreach (var coord in cells.Keys.Select(WorldConstants.WorldToChunk).Distinct())
            {
                var chunk = new ChunkData(coord);
                foreach (var cell in cells.Where(c => WorldConstants.WorldToChunk(c.Key).Equals(coord)))
                {
                    var local = WorldConstants.WorldToLocal(cell.Key);
                    chunk.Set(local.X, local.Y, local.Z, cell.Value);
                    if (shapes.TryGetValue(cell.Key, out int shape)) chunk.SetShape(local.X, local.Y, local.Z, shape);
                }
                var (render, collider) = ChunkMesher.Build(chunk, content, Cell, worldShape: Shape);
                _meshes.Add(render); _meshes.Add(collider);
                var go = new GameObject($"ShipChunk {coord.X},{coord.Y},{coord.Z}");
                go.transform.SetParent(ship.transform, false);
                var origin = WorldConstants.ChunkOrigin(coord);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z);
                go.AddComponent<MeshCollider>().sharedMesh = collider;
            }
            Box("Observed landing terrain", new Vector3(0f, 57.5f, 0f), new Vector3(80f, 1f, 80f));
            var game = _root.AddComponent<GameBootstrap>();
            var world = new ClientWorld();
            for (int x = -3; x <= 2; x++)
            for (int y = 2; y <= 4; y++)
            for (int z = -3; z <= 2; z++)
                world.StoreChunk(new ChunkCoord(x, y, z), new ushort[WorldConstants.BlocksPerChunk]);
            typeof(GameBootstrap).GetProperty("Content").SetValue(game, content);
            typeof(GameBootstrap).GetProperty("World").SetValue(game, world);
            _player.Game = game;
            // WorldRig's actual normal capsule dimensions and settings, not the smaller generic fixture.
            _capsule.height = 1.8f; _capsule.radius = 0.35f; _capsule.center = Vector3.up * 0.9f;
            _capsule.skinWidth = 0.03f; _capsule.stepOffset = 0.6f; _capsule.slopeLimit = 50f;
            _player.transform.position = new Vector3(0.51493406f, 59.03f, -2.8108604f);
            Physics.SyncTransforms();
        }

        private static Type Nested(string name) => typeof(PlayerController).Assembly.GetType("BlocksBeyondTheStars.Client.JourneyWalkPlanner+" + name);
        private static object NewMemory() => Activator.CreateInstance(Nested("Memory"), nonPublic: true);
        private static void Observe(object memory, Vector3 position) => memory.GetType().GetMethod("Observe").Invoke(memory, new object[] { position });
        private static string Status(object report) => (string)report.GetType().GetField("status").GetValue(report);
        private object NewSearch(Vector3 goal, object memory, int nodeBudget)
            => Activator.CreateInstance(Nested("Search"), new object[] { _player, _capsule, goal, 0.65f, true, memory, nodeBudget });
        private (List<Vector3> Route, object Report) Search(Vector3 goal, object memory = null, int nodeBudget = 2048)
        {
            var search = NewSearch(goal, memory ?? NewMemory(), nodeBudget);
            int batches = 0;
            while (!(bool)search.GetType().GetProperty("Done").GetValue(search) && batches++ < 128)
                search.GetType().GetMethod("Advance").Invoke(search, new object[] { 32 });
            Assert.IsTrue((bool)search.GetType().GetProperty("Done").GetValue(search), "The bounded search must terminate.");
            return ((List<Vector3>)search.GetType().GetField("Route").GetValue(search), search.GetType().GetField("Diagnostics").GetValue(search));
        }

        private void DeckAndGround()
        {
            Box("Deck", new Vector3(0f, 0.5f, -2f), new Vector3(20f, 1f, 4f));
            Box("Ground", new Vector3(0f, -0.5f, 3f), new Vector3(20f, 1f, 6f));
        }

        private List<Vector3> Plan(Vector3 goal)
        {
            var type = typeof(PlayerController).Assembly.GetType("BlocksBeyondTheStars.Client.JourneyWalkPlanner");
            return (List<Vector3>)type.GetMethod("Plan", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { _player, _capsule, goal });
        }

        private void Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }
    }
}
