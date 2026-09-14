// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class VeylSignalFxEditModeTests
    {
        private GameObject _root;
        private GameBootstrap _game;
        private ClientWorld _world;
        private VeylSignalFx _fx;
        private ClientSettings _settings;
        private NetworkClient _network;
        private LoopbackServerTransport _sender;
        private GameContent _content;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Veyl response verification");
            _game = _root.AddComponent<GameBootstrap>();
            _content = ContentLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "data"));
            _world = new ClientWorld();
            SetGame("Content", _content); SetGame("World", _world);
            _settings = new ClientSettings();
            _fx = _root.AddComponent<VeylSignalFx>();
            _fx.Initialize(_game, _settings); // Intentionally before the network exists and without Update.
            var link = new LoopbackLink();
            _network = new NetworkClient(new LoopbackClientTransport(link));
            _sender = new LoopbackServerTransport(link);
            _network.Connect("loopback", 31415);
            _sender.Poll(); _network.Poll();
            _network.BlockChanged += m => _world.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, m.Tint, m.Glow, m.Shape, out _);
            SetGame("Network", _network);
            // Exercise the exact early-ready event consumed by the rig-initialized component. No effect
            // Update is invoked before a server response can arrive on this first network frame.
            var ready = (Action<NetworkClient>)typeof(GameBootstrap).GetField("NetworkInitialized", Private).GetValue(_game);
            Assert.NotNull(ready);
            ready(_network);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            _network.Dispose(); _sender.Dispose();
        }

        private void SetGame(string property, object value) => typeof(GameBootstrap).GetProperty(property).SetValue(_game, value);
        private void Advance(float seconds) => typeof(VeylSignalFx).GetMethod("Advance", Private).Invoke(_fx, new object[] { seconds });
        private MeshFilter Filter() => _root.GetComponentInChildren<MeshFilter>();
        private void Send(object message)
        {
            _sender.Send(1, NetCodec.Encode(message), DeliveryMode.ReliableOrdered);
            _network.Poll();
        }
        private void Cell(int x, int y, int z, string key, int shape = 0)
        {
            var chunk = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
            if (!_world.TryGetChunk(chunk, out _)) _world.StoreChunk(chunk, new ushort[WorldConstants.BlocksPerChunk]);
            _world.ApplyBlockChange(x, y, z, key == "air" ? (ushort)0 : _content.GetBlock(key).NumericId.Value,
                0, 0, shape, out _);
        }
        private VeylSignalNode Rune(int x, int y, int z)
        {
            Cell(x, y, z, "rune_stone"); Cell(x, y, z - 1, "air");
            return new VeylSignalNode { X = x, Y = y, Z = z };
        }
        private static VeylSignalResponse Response(params VeylSignalNode[] nodes)
            => new VeylSignalResponse { EventId = "site-response", Nodes = nodes };

        [Test]
        public void FirstWireResponse_IsBoundBeforeUpdate_UsesOneBoundedMesh_AndDoesNotRebuildDuringAnimation()
        {
            var first = Rune(1, 1, 3); var second = Rune(1, 2, 3);
            Send(Response(first, second));
            var filter = Filter(); Assert.NotNull(filter);
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            var material = renderer.sharedMaterial;
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            Assert.AreEqual(40, mesh.vertexCount);
            Assert.AreEqual(1, mesh.subMeshCount);
            Assert.AreEqual(1, _root.GetComponentsInChildren<MeshRenderer>().Length);
            Assert.AreEqual(0, _root.GetComponentsInChildren<Collider>().Length);
            Assert.AreEqual("BlocksBeyondTheStars/VeylSignalPulse", material.shader.name);
            for (int i = 0; i < 30; i++) Advance(0.05f);
            Assert.AreSame(mesh, Filter().sharedMesh);
            Assert.AreSame(material, renderer.sharedMaterial);
            CollectionAssert.AreEqual(vertices, mesh.vertices);
            CollectionAssert.AreEqual(triangles, mesh.triangles);
            Assert.AreEqual("rune_stone", _content.BlockById(_world.GetBlock(first.X, first.Y, first.Z)).Key);
            Send(Response(first, second)); // Reliable duplicates do not restart or allocate a second renderer.
            Assert.AreSame(mesh, Filter().sharedMesh);
            Advance(1f);
            Assert.IsNull(Filter()); Assert.IsTrue(mesh == null); Assert.IsTrue(material == null);
            Send(Response(first, second));
            Assert.IsNull(Filter(), "A completed event cannot replay until a real world reset.");
        }

        [Test]
        public void RuntimeReducedEffects_UsesStationaryModeWithoutChangingGeometry_AndPauseDoesNotAdvance()
        {
            Send(Response(Rune(1, 1, 3), Rune(1, 2, 3)));
            var filter = Filter(); var mesh = filter.sharedMesh;
            var uv = mesh.uv;
            _settings.ReducedEffects = true;
            Advance(0.35f);
            var properties = new MaterialPropertyBlock();
            filter.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
            Assert.AreEqual(1f, properties.GetFloat("_ReducedEffects"));
            Assert.AreEqual(0.35f, properties.GetFloat("_Age"), 0.0001f);
            Advance(0f);
            filter.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
            Assert.AreEqual(0.35f, properties.GetFloat("_Age"), 0.0001f);
            CollectionAssert.AreEqual(uv, mesh.uv);
        }

        [Test]
        public void RemovedOrCoveredHostsDisappearWithoutMovingTheOtherRune_AndNeverResurrect()
        {
            var first = Rune(1, 1, 3); var second = Rune(1, 2, 3);
            Send(Response(first, second));
            var mesh = Filter().sharedMesh; var vertices = mesh.vertices;
            Send(new BlockChanged { X = first.X, Y = first.Y, Z = first.Z, Block = 0 });
            Assert.IsTrue(mesh.colors.Take(20).All(c => c.a == 0f));
            Assert.IsTrue(mesh.colors.Skip(20).All(c => c.a == 1f));
            Send(new BlockChanged { X = first.X, Y = first.Y, Z = first.Z, Block = _content.GetBlock("rune_stone").NumericId.Value });
            Advance(0.1f);
            Assert.IsTrue(mesh.colors.Take(20).All(c => c.a == 0f));
            CollectionAssert.AreEqual(vertices, mesh.vertices);
            Send(new BlockChanged { X = second.X, Y = second.Y, Z = second.Z - 1, Block = _content.GetBlock("stone").NumericId.Value });
            Assert.IsNull(Filter()); Assert.IsTrue(mesh == null);
        }

        [Test]
        public void UnknownCoveredShapedDistantOrOversizedSignalsCreateNoFloatingGeometry()
        {
            var covered = Rune(1, 1, 3); Cell(1, 1, 2, "stone");
            var shaped = Rune(2, 1, 3); Cell(2, 1, 3, "rune_stone", ShapeCode.Pack(BlockShape.Stairs, 0));
            var distant = Rune(200, 1, 3);
            Send(Response(covered, shaped, distant, new VeylSignalNode { X = 400, Y = 1, Z = 3 }));
            Assert.IsNull(Filter());
            var tooMany = new VeylSignalResponse
            {
                EventId = "oversized", Nodes = Enumerable.Range(0, 25).Select(i => Rune(i, 1, 4)).ToArray(),
            };
            Send(tooMany); Assert.IsNull(Filter());
        }

        [Test]
        public void BothWorldSeamsKeepTheInlaysTogetherAndOnTheRealNorthFaces()
        {
            int circumference = _game.Circumference;
            int half = WorldConstants.LatitudePeriodFor(circumference) / 2;
            _game.PlayerPosition = new Vector3(circumference - 0.5f, 1f, half - 0.5f);
            var first = Rune(circumference - 1, 1, half - 1);
            var second = Rune(0, 1, -half);
            Send(Response(first, second));
            var filter = Filter(); Assert.NotNull(filter);
            var mesh = filter.sharedMesh;
            Assert.Less(mesh.bounds.size.x, 1.5f);
            Assert.Less(mesh.bounds.size.z, 1.01f);
            float firstFace = _game.ScenePos(first.X, first.Y, first.Z).z - 0.008f;
            float secondFace = _game.ScenePos(second.X, second.Y, second.Z).z - 0.008f;
            Assert.AreEqual(firstFace, filter.transform.TransformPoint(mesh.vertices[0]).z, 0.001f);
            Assert.AreEqual(secondFace, filter.transform.TransformPoint(mesh.vertices[20]).z, 0.001f);
            _game.PlayerPosition += new Vector3(circumference, 0f, half * 2);
            Advance(0.1f);
            Assert.AreSame(mesh, filter.sharedMesh);
            Assert.AreEqual(firstFace + half * 2, filter.transform.TransformPoint(mesh.vertices[0]).z, 0.001f);
        }

        [Test]
        public void UnloadResetDisconnectAndDisableDisposeOwnedResources_WithoutLingeringListeners()
        {
            var node = Rune(1, 1, 3);
            Send(Response(node));
            var mesh = Filter().sharedMesh; var material = Filter().GetComponent<MeshRenderer>().sharedMaterial;
            _world.RemoveChunk(WorldConstants.WorldToChunk(new Vector3i(1, 1, 3)));
            Advance(0.1f);
            Assert.IsNull(Filter()); Assert.IsTrue(mesh == null); Assert.IsTrue(material == null);
            Send(new WorldReset());
            node = Rune(1, 1, 3); Send(Response(node));
            Assert.NotNull(Filter(), "Reset clears the old world's dedupe state.");
            Send(new WorldReset()); Assert.IsNull(Filter());
            Send(Response(node)); Assert.NotNull(Filter());
            _sender.DisconnectClient(1); _sender.Poll(); _network.Poll();
            Assert.IsNull(Filter());
            _fx.enabled = false;
            Send(new VeylSignalResponse { EventId = "disabled-view", Nodes = new[] { node } });
            Assert.IsNull(Filter(), "Disabled views must unsubscribe from the live client.");
        }
    }
}
