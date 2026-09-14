// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
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
