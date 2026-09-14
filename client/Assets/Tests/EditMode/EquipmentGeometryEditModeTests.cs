// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>Guards shared runtime resource ownership, mesh winding and the existing station footprint.</summary>
    public sealed class EquipmentGeometryEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Equipment geometry verification");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [TestCase(HeldItem.Kind.Scanner)]
        [TestCase(HeldItem.Kind.Drill)]
        public void SharedEquipmentSurvivesOneOwnerAndReleasesAfterLastOwner(HeldItem.Kind kind)
        {
            var tint = new Color(0.45f, 0.85f, 0.95f);
            var first = HeldItem.Build(_root.transform, kind, tint);
            var second = HeldItem.Build(_root.transform, kind, tint);
            var mesh = first.GetComponentInChildren<MeshFilter>().sharedMesh;
            var material = first.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            Assert.AreSame(mesh, second.GetComponentInChildren<MeshFilter>().sharedMesh);
            Assert.AreSame(material, second.GetComponentInChildren<MeshRenderer>().sharedMaterial);
            Assert.LessOrEqual(first.GetComponentsInChildren<Renderer>().Length, 2, "Equipment must combine details into a bounded number of draws.");
            Assert.IsEmpty(first.GetComponentsInChildren<Collider>());
            AssertValidGeometry(first);

            Object.DestroyImmediate(first);
            Assert.IsTrue(mesh != null, "The other viewmodel still owns the shared mesh.");
            Assert.IsTrue(material != null);
            Object.DestroyImmediate(second);
            Assert.IsTrue(mesh == null, "The final lease must release the generated mesh.");
            Assert.IsTrue(material == null, "Switching away from equipment must release generated materials.");
        }

        [TestCase("cockpit")]
        [TestCase("workshop")]
        [TestCase("lab")]
        [TestCase("console")]
        [TestCase("medbay")]
        [TestCase("life_support")]
        [TestCase("quarters")]
        [TestCase("cargo")]
        public void StationFixturesStayInsideTheirSolidMarkerFootprint(string type)
        {
            var builder = typeof(StationDecorView).GetMethod("BuildModel", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(builder);
            var fixture = (GameObject)builder.Invoke(null, new object[] { _root.transform, type });
            Assert.NotNull(fixture);
            Assert.IsEmpty(fixture.GetComponentsInChildren<Collider>());
            Assert.LessOrEqual(fixture.GetComponentsInChildren<Renderer>().Length, 2);
            foreach (var filter in fixture.GetComponentsInChildren<MeshFilter>())
            {
                var bounds = filter.sharedMesh.bounds;
                Assert.GreaterOrEqual(bounds.min.x, -0.48f, type + " crosses a neighboring walkway.");
                Assert.LessOrEqual(bounds.max.x, 0.48f);
                Assert.GreaterOrEqual(bounds.min.z, -0.48f);
                Assert.LessOrEqual(bounds.max.z, 0.48f);
                Assert.GreaterOrEqual(bounds.min.y, -0.001f);
                Assert.LessOrEqual(bounds.max.y, 0.92f, type + " requires more headroom than the existing fixture envelope.");
            }

            AssertValidGeometry(fixture);
        }

        [Test]
        public void EquipmentBuiltUnderHiddenViewmodelStillReleasesItsLease()
        {
            _root.SetActive(false);
            var held = HeldItem.Build(_root.transform, HeldItem.Kind.Scanner, Color.cyan);
            var mesh = held.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            var material = held.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            Object.DestroyImmediate(held);
            Assert.IsTrue(mesh == null, "Equipment switched while the viewmodel is hidden must not leak.");
            Assert.IsTrue(material == null);
        }

        [Test]
        public void SurveySpecimenSharesResourcesAndFitsItsWorkshopDock()
        {
            var builder = typeof(StationDecorView).GetMethod("BuildSurveySpecimen", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(builder);
            var first = (GameObject)builder.Invoke(null, new object[] { _root.transform });
            var second = (GameObject)builder.Invoke(null, new object[] { _root.transform });
            var mesh = first.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.AreSame(mesh, second.GetComponentInChildren<MeshFilter>().sharedMesh);
            Assert.IsEmpty(first.GetComponentsInChildren<Collider>());
            foreach (var filter in first.GetComponentsInChildren<MeshFilter>())
            {
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = _root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    Assert.That(point.x, Is.InRange(0.10f, 0.30f));
                    Assert.That(point.y, Is.InRange(0.31f, 0.50f));
                    Assert.That(point.z, Is.InRange(-0.34f, -0.16f));
                }
            }

            AssertValidGeometry(first);
            Object.DestroyImmediate(first);
            Assert.IsTrue(mesh != null);
            Object.DestroyImmediate(second);
            Assert.IsTrue(mesh == null);
        }

        [Test]
        public void FirstPersonGripHandIsCompactColliderFreeAndShared()
        {
            var builder = typeof(HeldItem).GetMethod("BuildGripHand", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(builder);
            var first = (GameObject)builder.Invoke(null, new object[] { _root.transform, HeldItem.Kind.Drill, Color.gray });
            var second = (GameObject)builder.Invoke(null, new object[] { _root.transform, HeldItem.Kind.Drill, Color.gray });
            var mesh = first.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.AreSame(mesh, second.GetComponentInChildren<MeshFilter>().sharedMesh);
            Assert.Less(mesh.bounds.size.magnitude, 0.55f, "Glove must use hand-scale meters, not avatar-root scaling.");
            Assert.LessOrEqual(first.GetComponentsInChildren<Renderer>().Length, 1);
            Assert.IsEmpty(first.GetComponentsInChildren<Collider>());
            AssertValidGeometry(first);
            Object.DestroyImmediate(first);
            Assert.IsTrue(mesh != null);
            Object.DestroyImmediate(second);
            Assert.IsTrue(mesh == null);
        }

        [Test]
        public void HeldBlockKeepsAtlasMappingAndDoesNotOwnTheWorldTexture()
        {
            var previous = HeldItem.BlockTileResolver;
            var texture = new Texture2D(16, 16);
            try
            {
                HeldItem.BlockTileResolver = _ => (texture, new Rect(0.25f, 0.5f, 0.125f, 0.125f));
                var block = HeldItem.Build(_root.transform, HeldItem.Kind.Block, Color.white, "basalt");
                var material = block.GetComponentInChildren<Renderer>().sharedMaterial;
                Assert.AreSame(texture, material.mainTexture);
                Assert.AreEqual(new Vector2(0.125f, 0.125f), material.mainTextureScale);
                Assert.AreEqual(new Vector2(0.25f, 0.5f), material.mainTextureOffset);
                Assert.IsEmpty(block.GetComponentsInChildren<Collider>());
                Object.DestroyImmediate(block);
                Assert.IsTrue(material == null);
                Assert.IsTrue(texture != null, "World atlas ownership stays with GameBootstrap.");
            }
            finally
            {
                HeldItem.BlockTileResolver = previous;
                Object.DestroyImmediate(texture);
            }
        }

        private static void AssertValidGeometry(GameObject root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var triangles = mesh.triangles;
                Assert.Greater(triangles.Length, 0);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    Assert.Greater(face.sqrMagnitude, 1e-14f, mesh.name + " contains a degenerate triangle.");
                    // Vector3.normalized treats lengths below 1e-5 as zero, but a valid 2 mm bevel
                    // has a cross-product magnitude around 7e-6. Keep the strict orientation check and
                    // normalize explicitly after the independent non-degeneracy assertion above.
                    var faceNormal = face / Mathf.Sqrt(face.sqrMagnitude);
                    Assert.Greater(Vector3.Dot(faceNormal, normals[a]), 0.9f,
                        mesh.name + " triangle " + i / 3 + " has inward winding or incorrect bevel normals.");
                }
            }
        }
    }
}
