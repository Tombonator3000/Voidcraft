// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class DoorViewEditModeTests
    {
        private GameObject _root;
        private DoorView _view;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Door verification");
            _view = _root.AddComponent<DoorView>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [TestCase("slide", 1f, 0.5f, false)]
        [TestCase("slide", 1f, 0.90f, true)]
        [TestCase("slide", 3f, 0.5f, true)]
        [TestCase("energy", 1f, 0f, false)]
        [TestCase("energy", 1f, 1f, true)]
        [TestCase("hinge", 1f, 0.5f, false)]
        [TestCase("hinge", 1f, 1f, true)]
        [TestCase("wood", 1f, 1f, true)]
        public void PassageRequiresActualCapsuleClearance(string kind, float width, float animation, bool expected)
        {
            var method = typeof(DoorView).GetMethod("HasPassageClearance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.AreEqual(expected, method.Invoke(null, new object[] { kind, width, animation }));
        }

        [TestCase("slide", false)]
        [TestCase("slide", true)]
        [TestCase("hinge", false)]
        [TestCase("hinge", true)]
        public void InitialColliderMatchesReceivedOpenState(string kind, bool open)
        {
            Receive(new NetDoor { Id = 1, Kind = kind, AxisX = true, Width = 1f, Open = open });
            var colliders = _root.GetComponentsInChildren<Collider>();
            Assert.AreEqual(1, colliders.Length, "Visual panels must not add phantom colliders.");
            Assert.AreEqual(!open, colliders[0].enabled);
        }

        [Test]
        public void OutboundAndHomecomingJourneyWaitForEachAnimatedEnergyHatchOpening()
        {
            var hatch = new NetDoor { Id = 70, Kind = "energy", AxisX = true, Width = 2f, Open = false };
            Receive(hatch);
            var collider = _root.GetComponentInChildren<BoxCollider>();
            Assert.IsTrue(collider.enabled);
            Assert.IsFalse(JourneyPassageReady(hatch));

            hatch.Open = true;
            Receive(hatch);
            Assert.IsTrue(collider.enabled, "The server-open packet arrives before the panels have cleared the capsule.");
            Assert.IsFalse(JourneyPassageReady(hatch),
                "The journey must not plan through the hatch while its real collider still blocks the route.");

            for (int frame = 0; frame < 4; frame++) AdvanceDoors(1f / 60f);
            Assert.IsTrue(collider.enabled, "At 40% open the two-metre hatch is still narrower than the capsule clearance.");
            Assert.IsFalse(JourneyPassageReady(hatch));

            AdvanceDoors(1f / 60f);
            Assert.IsFalse(collider.enabled);
            Assert.IsTrue(JourneyPassageReady(hatch),
                "The outbound journey can proceed once the animated view has actually released its collider.");

            hatch.Open = false;
            Receive(hatch);
            Assert.IsFalse(JourneyPassageReady(hatch),
                "A closing server state cannot start home entry even while the local panels are still retracting.");
            for (int frame = 0; frame < 5; frame++) AdvanceDoors(1f / 60f);
            Assert.IsTrue(collider.enabled);

            hatch.Open = true;
            Receive(hatch);
            Assert.IsFalse(JourneyPassageReady(hatch),
                "Homecoming must wait through the same real collider transition as the outbound route.");
            for (int frame = 0; frame < 5; frame++) AdvanceDoors(1f / 60f);
            Assert.IsFalse(collider.enabled);
            Assert.IsTrue(JourneyPassageReady(hatch),
                "Home entry can proceed only after the reopened hatch has physically cleared the capsule.");
        }

        [Test]
        public void ReusedRegistryIdRebuildsPositionAxisAndReleasesOldResources()
        {
            Receive(new NetDoor { Id = 4, Kind = "energy", X = 2f, Y = 3f, Z = 4f, AxisX = true, Width = 1f });
            var oldDoor = _root.transform.GetChild(0).gameObject;
            var oldMesh = oldDoor.GetComponentInChildren<MeshFilter>().sharedMesh;
            var oldRenderers = oldDoor.GetComponentsInChildren<Renderer>();
            var oldMaterials = new Material[oldRenderers.Length];
            for (int i = 0; i < oldRenderers.Length; i++)
            {
                oldMaterials[i] = oldRenderers[i].sharedMaterial;
            }

            Receive(new NetDoor { Id = 4, Kind = "slide", X = 12f, Y = 5f, Z = 7f, AxisX = false, Width = 3f });
            Assert.IsTrue(oldDoor == null);
            Assert.IsTrue(oldMesh == null);
            foreach (var material in oldMaterials)
            {
                Assert.IsTrue(material == null, "Removing an energy door must release its field and cached panel materials.");
            }

            Assert.AreEqual(1, _root.transform.childCount);
            var replacement = _root.transform.GetChild(0);
            Assert.AreEqual(new Vector3(12f, 5f, 7f), replacement.position);
            var collider = replacement.GetComponent<BoxCollider>();
            Assert.AreEqual(3f, collider.size.z, 0.001f);
            Assert.Less(collider.size.x, 0.4f);
            Assert.IsTrue(collider.enabled);

            Receive();
            Assert.AreEqual(0, _root.transform.childCount);
            Assert.IsTrue(collider == null);
        }

        private void Receive(params NetDoor[] doors)
        {
            var method = typeof(DoorView).GetMethod("OnDoors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(_view, new object[] { new DoorList { Doors = doors } });
        }

        private void AdvanceDoors(float deltaTime)
        {
            var method = typeof(DoorView).GetMethod("AdvanceDoors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(_view, new object[] { deltaTime });
        }

        private bool JourneyPassageReady(NetDoor door)
        {
            var method = typeof(SurveyJourneyProbe).GetMethod("DoorPassageReady", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (bool)method.Invoke(null, new object[] { door, _view });
        }
    }
}
