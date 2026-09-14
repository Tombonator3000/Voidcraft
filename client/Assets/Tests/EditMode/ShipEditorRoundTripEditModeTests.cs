// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class ShipEditorRoundTripEditModeTests
    {
        [Serializable] private sealed class Cell { public int x, y, z; public string kind, id; public int tint, glow, shape, yaw; }
        [Serializable] private sealed class Spawn { public int x, y, z; }
        [Serializable] private sealed class Layout
        {
            public int width, height, length;
            public bool preserveAuthoredFinishes;
            public Spawn spawn;
            public List<Cell> cells;
        }

        [Test]
        public void ImportAndActualExportPreserveHomeGeometryFacingSpawnAndAuthoredLighting()
        {
            string json = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../data/ship_layouts/ship_starter_home.json")));
            var expected = JsonUtility.FromJson<Layout>(json);
            string key = "roundtrip_verification_" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Application.persistentDataPath, "ship_exports", key);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "layout.json"), json);
            var root = new GameObject("Authored ship roundtrip verification");
            root.SetActive(false); // no editor camera/UI or Start side effects are needed for an import/export
            var editor = root.AddComponent<ShipEditor>();
            object view = null;
            try
            {
                var type = typeof(ShipEditor);
                var assembly = type.Assembly;
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                // Confirm the real palette admits both forward work stations; neither may be dropped on import.
                var builtPalette = (Array)type.GetMethod("BuildPalette", Private).Invoke(editor, null);
                var entryType = builtPalette.GetType().GetElementType();
                var paletteIds = builtPalette.Cast<object>().Select(e => (string)entryType.GetField("Id").GetValue(e)).ToArray();
                CollectionAssert.Contains(paletteIds, "lab");
                CollectionAssert.Contains(paletteIds, "console");

                // Supply the same content IDs without opening AppShell or its world/network lifecycle.
                var ids = expected.cells.GroupBy(c => c.id).Select(g => g.First()).ToArray();
                var palette = Array.CreateInstance(entryType, ids.Length);
                for (int i = 0; i < ids.Length; i++)
                {
                    var entry = Activator.CreateInstance(entryType);
                    entryType.GetField("Id").SetValue(entry, ids[i].id);
                    entryType.GetField("Kind").SetValue(entry, ids[i].kind);
                    entryType.GetField("Color").SetValue(entry, Color.gray);
                    palette.SetValue(entry, i);
                }
                type.GetField("_palette", Private).SetValue(editor, palette);
                var viewType = assembly.GetType("BlocksBeyondTheStars.Client.EditorVoxelChunkView");
                view = Activator.CreateInstance(viewType, new object[] { root.transform });
                type.GetField("_view", Private).SetValue(editor, view);
                type.GetMethod("LoadDesign", Private).Invoke(editor, new object[] { key });
                var exported = type.GetMethod("BuildLayoutExport", Private).Invoke(editor, null);
                var actual = JsonUtility.FromJson<Layout>(JsonUtility.ToJson(exported));

                Assert.AreEqual(6, actual.width);
                Assert.AreEqual(4, actual.height, "Import/export must not increment the authored roof convention.");
                Assert.AreEqual(11, actual.length);
                Assert.IsTrue(actual.preserveAuthoredFinishes);
                Assert.AreEqual(JsonUtility.ToJson(expected.spawn), JsonUtility.ToJson(actual.spawn));
                CollectionAssert.AreEquivalent(expected.cells.Select(c => JsonUtility.ToJson(c)).ToArray(),
                    actual.cells.Select(c => JsonUtility.ToJson(c)).ToArray());
                Assert.AreEqual(270, actual.cells.Single(c => c.id == "workshop").yaw);
                Assert.AreEqual(90, actual.cells.Single(c => c.id == "quarters").yaw);
                Assert.IsTrue(actual.cells.Any(c => c.z == -1 && c.shape != 0), "Threshold and engine attachments survive.");
            }
            finally
            {
                // The production editor disposes during PlayMode. This test owns its EditMode resources.
                typeof(ShipEditor).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, null);
                foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Distinct())
                    if (mesh != null) Object.DestroyImmediate(mesh);
                if (view != null)
                {
                    var material = (Material)view.GetType().GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                    if (material != null) Object.DestroyImmediate(material);
                }
                Object.DestroyImmediate(root);
                Directory.Delete(directory, true);
            }
        }
    }
}
