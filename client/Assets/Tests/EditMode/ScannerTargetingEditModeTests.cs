// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class ScannerTargetingEditModeTests
    {
        private GameObject _root;
        private GameBootstrap _game;
        private PlayerController _player;
        private MicroFaunaView _fauna, _previousFauna;
        private GameContent _content;

        [SetUp]
        public void SetUp()
        {
            _content = ContentLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "data"));
            _root = new GameObject("Scanner target fixture");
            _game = _root.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetProperty("Content").SetValue(_game, _content);
            var world = new ClientWorld();
            world.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[WorldConstants.BlocksPerChunk]);
            typeof(GameBootstrap).GetProperty("World").SetValue(_game, world);
            var body = new GameObject("Player");
            body.transform.SetParent(_root.transform);
            body.AddComponent<CharacterController>();
            _player = body.AddComponent<PlayerController>();
            _player.Game = _game;
            var eye = new GameObject("Eye");
            eye.transform.SetParent(body.transform);
            eye.transform.position = new Vector3(0.5f, 1.5f, 0.5f);
            _player.Camera = eye.AddComponent<Camera>();
            _previousFauna = MicroFaunaView.Instance;
            _fauna = _root.AddComponent<MicroFaunaView>();
            _fauna.Game = _game;
            // EditMode does not run this non-ExecuteAlways view's Awake. Arrange its runtime
            // registration without generating the cosmetic atlas or relying on a prior scene view.
            typeof(MicroFaunaView).GetProperty(nameof(MicroFaunaView.Instance)).SetValue(null, _fauna);
            Assert.AreSame(_fauna, MicroFaunaView.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            typeof(MicroFaunaView).GetProperty(nameof(MicroFaunaView.Instance)).SetValue(null, _previousFauna);
        }

        [Test]
        public void NearbyOffAxisAndBehindCreaturesDoNotStealTheAimedRune()
        {
            Block("rune_stone", 5);
            Creatures(Creature("off_axis", 3f, 1f), Creature("behind", 0.5f, -2f));
            AssertTarget("block", "rune_stone");
        }

        [Test]
        public void TheNearestAimedBodyWinsEvenWhenItsCenterIsPastTheBlockFace()
        {
            Block("rune_stone", 5);
            Creatures(Creature("far", 0.5f, 7f), Creature("visible_body", 0.5f, 5.3f));
            AssertTarget("creature", "visible_body");
        }

        [TestCase("stone")]
        [TestCase("water")]
        [TestCase("lava")]
        public void TheFirstAimedVoxelOccludesLifeBehindItIncludingColliderlessFluids(string material)
        {
            Block(material, 3);
            Creatures(Creature("hidden", 0.5f, 5f));
            Critter("hidden_bug", new Vector3(0.5f, 1.5f, 4f));
            AssertTarget("block", material);
        }

        [Test]
        public void TinyAimedLifeUsesTheSameDistanceOrderingInsteadOfNearestAroundThePlayer()
        {
            Block("rune_stone", 5);
            Creatures(Creature("aimed_large", 0.5f, 4f));
            Critter("off_axis_bug", new Vector3(1.5f, 1.5f, 1f));
            Critter("aimed_bug", new Vector3(0.5f, 1.5f, 2f));
            AssertTarget("microfauna", "aimed_bug");
        }

        [Test]
        public void EmptyRayAndOutOfReachLifeSelectNothing()
        {
            Creatures(Creature("behind", 0.5f, -2f), Creature("far", 0.5f, 10f));
            Critter("far_bug", new Vector3(0.5f, 1.5f, 6f));
            Assert.IsFalse(_player.TryGetScanTarget(out _, out _, out _));
        }

        private void Block(string key, int z)
            => _game.World.ApplyBlockChange(0, 1, z, _content.GetBlock(key).NumericId.Value, out _);

        private void Creatures(params NetCreature[] creatures)
            => typeof(GameBootstrap).GetProperty("Creatures").SetValue(_game, creatures);

        private static NetCreature Creature(string species, float x, float z)
            => new() { SpeciesId = species, X = x, Y = 0.9f, Z = z, Size = 1f };

        private void Critter(string key, Vector3 position)
        {
            var type = typeof(MicroFaunaView).GetNestedType("Critter", BindingFlags.NonPublic);
            var critter = Activator.CreateInstance(type);
            type.GetField("WorldPos").SetValue(critter, position);
            type.GetField("SizeScale").SetValue(critter, 1f);
            type.GetField("Kind").SetValue(critter, new CritterKind(key, 0, default, default, false, 0.1f, 1f, false, Array.Empty<Color>()));
            ((IList)typeof(MicroFaunaView).GetField("_alive", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_fauna)).Add(critter);
        }

        private void AssertTarget(string kind, string key)
        {
            Assert.IsTrue(_player.TryGetScanTarget(out var actualKind, out var actualKey, out _));
            Assert.AreEqual(kind, actualKind);
            Assert.AreEqual(key, actualKey);
        }
    }
}
