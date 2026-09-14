// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
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
