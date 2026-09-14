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
    public sealed class StationPickingEditModeTests
    {
        private GameObject _root;
        private GameBootstrap _game;
        private StationDecorView _view;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Station picking verification");
            _view = _root.AddComponent<StationDecorView>();
            var link = new GameObject("Server link");
            link.transform.SetParent(_root.transform, false);
            _game = link.AddComponent<GameBootstrap>();
            _view.Game = _game;
            var cameraObject = new GameObject("Picking camera");
            cameraObject.transform.SetParent(_root.transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 1.55f, -2f);
            _camera.transform.rotation = Quaternion.identity;
            SetStations(new[]
            {
                new NetShipStation { Type = "console", X = 0f, Y = 0f, Z = 0f },
                new NetShipStation { Type = "medbay", X = 0f, Y = 0f, Z = 0.9f },
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Physics.SyncTransforms();
        }

        [Test]
        public void AimingAtTheAftHatchClearsTheNearbyCockpitPrompt()
        {
            SetStations(new[] { new NetShipStation { Type = "cockpit", X = 0f, Y = 0f, Z = 0f } });
            var rig = new GameObject("Player prompt verification");
            rig.transform.SetParent(_root.transform, false);
            rig.transform.position = new Vector3(0f, 0f, -2f);
            var player = rig.AddComponent<PlayerController>();
            rig.GetComponent<CharacterController>().enabled = false;
            player.Game = _game;
            player.Camera = _camera;
            var refresh = typeof(PlayerController).GetMethod("RefreshStationPrompt", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(refresh);
            refresh.Invoke(player, null);
            Assert.AreEqual("cockpit", _game.NearbyStation);
            Assert.AreEqual("cockpit", _game.NearestStationType(rig.transform.position, 3f),
                "The off-screen cockpit really is nearby; removing the stale prompt must not rely on distance.");

            Box("Aft hatch", new Vector3(0f, 1.4f, -4f), new Vector3(2f, 2.8f, 0.18f));
            _camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Physics.SyncTransforms();
            refresh.Invoke(player, null);
            Assert.AreEqual(string.Empty, _game.NearbyStation,
                "Facing the hatch must not advertise or interact with the cockpit behind the player.");
        }

        [Test]
        public void AdjacentWallIsNotAStationMarkerButTheActualPedestalRemainsSelectable()
        {
            SetStations(new[] { new NetShipStation { Type = "console", X = 0f, Y = 0f, Z = 0f } });
            Box("Station pedestal", new Vector3(0f, 0.5f, 0f), Vector3.one);
            Box("Adjacent wall", new Vector3(1f, 0.5f, 0f), Vector3.one);
            _camera.transform.position = new Vector3(1f, 0.5f, -2f);
            Physics.SyncTransforms();
            Assert.AreEqual(string.Empty, _game.LookedStationType(_camera, 4f));
            _camera.transform.position = new Vector3(0f, 0.5f, -2f);
            Assert.AreEqual("console", _game.LookedStationType(_camera, 4f));
        }

        [Test]
        public void VisibleMonitorWinsOverTheMarkerClosestToTheWallBehindIt()
        {
            Box("Wall behind console", new Vector3(0f, 1.5f, 0.65f), new Vector3(3f, 3f, 0.1f));
            Physics.SyncTransforms();
            Assert.AreEqual("console", _game.LookedStationType(_camera, 4f),
                "The physics ray hits near medbay, but the player is visibly aiming at the console in front.");
        }

        [Test]
        public void ClosedDoorOccludesTheVisibleFixtureWithoutReselectingItsMarker()
        {
            Box("Closed door", new Vector3(0f, 1.5f, -0.8f), new Vector3(1f, 2.8f, 0.18f));
            Physics.SyncTransforms();
            Assert.AreEqual(string.Empty, _game.LookedStationType(_camera, 4f));
        }

        [Test]
        public void TriggerVolumesDoNotHideTheMonitorAndRangeStillApplies()
        {
            Box("Nonblocking trigger", new Vector3(0f, 1.5f, -0.8f), new Vector3(1f, 2.8f, 0.18f)).isTrigger = true;
            Physics.SyncTransforms();
            Assert.AreEqual("console", _game.LookedStationType(_camera, 4f));
            Assert.AreEqual(string.Empty, _game.LookedStationType(_camera, 1f));
        }

        [Test]
        public void ReplacingStationsRebuildsPickingBeforeTheNextViewUpdate()
        {
            Assert.AreEqual("console", _game.LookedStationType(_camera, 4f));
            SetStations(new[] { new NetShipStation { Type = "lab", X = 0f, Y = 0f, Z = 0f } });
            Assert.AreEqual("lab", _game.LookedStationType(_camera, 4f));
        }

        [Test]
        public void PickerRejectsAnotherGameOwnerAndDoesNotIncludeProjectionChildren()
        {
            SetStations(new[] { new NetShipStation { Type = "cockpit", X = 0f, Y = 0f, Z = 0f } });
            _game.LookedStationType(_camera, 4f); // builds and caches the opaque housing
            var projection = GameObject.CreatePrimitive(PrimitiveType.Cube);
            projection.name = "Test hologram";
            Object.DestroyImmediate(projection.GetComponent<Collider>());
            projection.transform.SetParent(_root.transform.Find("cockpit fixture"), false);
            projection.transform.position = new Vector3(0f, 1.8f, -0.1f);
            projection.transform.localScale = Vector3.one * 0.12f;
            _camera.transform.position = new Vector3(0f, 1.8f, -2f);
            Assert.AreEqual(string.Empty, _game.LookedStationType(_camera, 4f), "A floating projection is not a station housing.");

            var otherLink = new GameObject("Unrelated game");
            otherLink.transform.SetParent(_root.transform, false);
            var other = otherLink.AddComponent<GameBootstrap>();
            var method = typeof(StationDecorView).GetMethod("TryPick", BindingFlags.Instance | BindingFlags.NonPublic);
            var args = new object[] { other, new Ray(new Vector3(0f, 1.55f, -2f), Vector3.forward), 4f, 4f, null, false };
            Assert.IsFalse((bool)method.Invoke(_view, args));
            Assert.AreEqual(string.Empty, args[4]);
        }

        private void SetStations(NetShipStation[] stations)
            => typeof(GameBootstrap).GetProperty("Stations").SetValue(_game, stations);

        [Test]
        public void SideFacingWorkshopRotatesItsActualHousingAndCachedPickBounds()
        {
            SetStations(new[] { new NetShipStation { Type = "workshop", X = 0f, Y = 0f, Z = 0f, Yaw = 270 } });
            _camera.transform.position = new Vector3(2f, 1.6f, 0f);
            _camera.transform.LookAt(new Vector3(0f, 1.6f, 0f));
            Assert.AreEqual("workshop", _game.LookedStationType(_camera, 3f));
            var fixture = _root.transform.Find("workshop fixture");
            Assert.IsNotNull(fixture);
            Assert.That(Vector3.Dot(fixture.TransformDirection(Vector3.back), Vector3.right), Is.GreaterThan(0.999f));
            Assert.IsEmpty(fixture.GetComponentsInChildren<Collider>());
            var bounds = fixture.Find("Housing").GetComponent<Renderer>().bounds;
            Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(-0.48f));
            Assert.That(bounds.max.x, Is.LessThanOrEqualTo(0.48f));
            Assert.That(bounds.min.z, Is.GreaterThanOrEqualTo(-0.48f));
            Assert.That(bounds.max.z, Is.LessThanOrEqualTo(0.48f));
        }

        private BoxCollider Box(string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }
    }
}
