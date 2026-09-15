// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Compact, code-authored equipment with real bevel normals. All solid details share one palette
    /// texture and draw; narrow emissive faces share a second draw. Instances borrow the same meshes
    /// and materials until their last owner is destroyed. No colliders or world state are created.
    /// </summary>
    internal static class EquipmentGeometry
    {
        internal enum Finish { Ceramic, Graphite, Steel, Rubber, Orange, Glass, Fabric, Linen, Ore, Crystal, Tint }

        private sealed class Model
        {
            internal Mesh Solid;
            internal Mesh Signals;
            internal Material Surface;
            internal Material Glow;
            internal Texture2D Palette;
            internal int Users;
        }

        private static readonly Dictionary<string, Model> Models = new();

        internal static GameObject Create(Transform parent, string name, string key, Color tint, Action<Builder> build)
        {
            if (!Models.TryGetValue(key, out var model))
            {
                var builder = new Builder();
                build(builder);
                model = new Model { Solid = builder.Solid.ToMesh(name + " surface"), Signals = builder.Signals.ToMesh(name + " signals") };
                model.Palette = CreatePalette(tint);
                model.Surface = new Material(Shader.Find("BlocksBeyondTheStars/EquipmentSurface"))
                {
                    name = name + " ceramic and graphite",
                    color = Color.white,
                    mainTexture = model.Palette,
                };
                if (model.Signals != null)
                {
                    model.Glow = new Material(Shader.Find("BlocksBeyondTheStars/Particle")) { name = name + " signal inlays" };
                }
                Models.Add(key, model);
            }

            model.Users++;
            var root = new GameObject(name);
            // Initialize ownership while the new object is active, before parenting to a possibly hidden
            // viewmodel. Unity does not promise OnDestroy on a component that has never been active.
            root.AddComponent<EquipmentGeometryLease>().Key = key;
            root.transform.SetParent(parent, false);
            AddMesh(root.transform, "Housing", model.Solid, model.Surface, true);
            AddMesh(root.transform, "SignalInlays", model.Signals, model.Glow, false);
            return root;
        }

        private static void AddMesh(Transform parent, string name, Mesh mesh, Material material, bool shadows)
        {
            if (mesh == null)
            {
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
        }

        private static Texture2D CreatePalette(Color tint)
        {
            // Texture pixels are authored in sRGB and sampled through Unity's sRGB texture conversion.
            // Unlike raw shader uniforms/vertex colours, converting these with ShaderColor would double it.
            var pixels = new[]
            {
                new Color(0.80f, 0.79f, 0.73f), new Color(0.21f, 0.25f, 0.29f),
                new Color(0.53f, 0.60f, 0.64f), new Color(0.10f, 0.13f, 0.16f),
                new Color(0.76f, 0.39f, 0.17f), new Color(0.035f, 0.14f, 0.20f),
                new Color(0.54f, 0.29f, 0.15f), new Color(0.53f, 0.54f, 0.50f),
                new Color(0.57f, 0.44f, 0.25f), new Color(0.43f, 0.39f, 0.57f), tint,
                Color.white, Color.white, Color.white, Color.white, Color.white,
            };
            var texture = new Texture2D(16, 1, TextureFormat.RGBA32, false, false)
            {
                name = "Equipment material palette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        internal static void Release(string key)
        {
            if (key == null || !Models.TryGetValue(key, out var model) || --model.Users > 0)
            {
                return;
            }

            Models.Remove(key);
            DestroyResource(model.Solid);
            DestroyResource(model.Signals);
            DestroyResource(model.Surface);
            DestroyResource(model.Glow);
            DestroyResource(model.Palette);
        }

        internal static void DestroyResource(Object resource)
        {
            if (resource == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(resource);
            }
            else
            {
                Object.DestroyImmediate(resource);
            }
        }

        internal sealed class MeshData
        {
            private readonly bool _surface;
            private readonly List<Vector3> _vertices = new();
            private readonly List<Vector3> _normals = new();
            private readonly List<Vector2> _uv = new();
            private readonly List<Color> _colors = new();
            private readonly List<int> _triangles = new();

            internal MeshData(bool surface = false) => _surface = surface;

            internal void Face(Vector3[] points, Vector3 normal, Finish finish, Color color, Vector3 position, Quaternion rotation)
            {
                int first = _vertices.Count;
                bool reverse = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f;
                for (int i = 0; i < points.Length; i++)
                {
                    _vertices.Add(position + rotation * points[i]);
                    _normals.Add(rotation * normal.normalized);
                    _uv.Add(new Vector2(((int)finish + 0.5f) / 16f, 0.5f));
                    _colors.Add(_surface ? SurfaceResponse(finish) : color);
                }

                for (int i = 1; i < points.Length - 1; i++)
                {
                    _triangles.Add(first);
                    _triangles.Add(first + (reverse ? i + 1 : i));
                    _triangles.Add(first + (reverse ? i : i + 1));
                }
            }

            private static Color SurfaceResponse(Finish finish) => finish switch
            {
                Finish.Ceramic => new Color(0.65f, 0.05f, 1f, 0f),
                Finish.Graphite => new Color(0.56f, 0.50f, 1f, 0f),
                Finish.Steel => new Color(0.30f, 0.85f, 1f, 0f),
                Finish.Rubber => new Color(0.88f, 0f, 1f, 0f),
                Finish.Orange => new Color(0.60f, 0.05f, 1f, 0f),
                Finish.Glass => new Color(0.18f, 0.20f, 1f, 0f),
                Finish.Fabric or Finish.Linen => new Color(0.92f, 0f, 1f, 0f),
                Finish.Ore => new Color(0.80f, 0.25f, 1f, 0f),
                Finish.Crystal => new Color(0.30f, 0.05f, 1f, 0f),
                _ => new Color(0.65f, 0.05f, 1f, 0f),
            };

            internal Mesh ToMesh(string name)
            {
                if (_vertices.Count == 0)
                {
                    return null;
                }

                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65535)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uv);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        internal sealed class Builder
        {
            internal readonly MeshData Solid = new(true);
            internal readonly MeshData Signals = new();

            /// <summary>A chamfered cuboid: six inset faces, twelve bevel strips and eight corner caps.</summary>
            internal void Box(Vector3 position, Vector3 size, Finish finish, float bevel = 0f, Quaternion? rotation = null)
            {
                var q = rotation ?? Quaternion.identity;
                Vector3 half = size * 0.5f;
                float b = Mathf.Clamp(bevel, 0f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.85f);
                for (int axis = 0; axis < 3; axis++)
                {
                    int a = (axis + 1) % 3, c = (axis + 2) % 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        var normal = Vector3.zero;
                        normal[axis] = sign;
                        var points = new Vector3[4];
                        for (int i = 0; i < 4; i++)
                        {
                            points[i][axis] = sign * half[axis];
                            points[i][a] = (i == 0 || i == 3 ? -1f : 1f) * (half[a] - b);
                            points[i][c] = (i < 2 ? -1f : 1f) * (half[c] - b);
                        }

                        Solid.Face(points, normal, finish, Color.white, position, q);
                    }
                }

                if (b <= 0f)
                {
                    return;
                }

                for (int axis = 0; axis < 3; axis++)
                {
                    int a = (axis + 1) % 3, c = (axis + 2) % 3;
                    for (int sa = -1; sa <= 1; sa += 2)
                    {
                        for (int sc = -1; sc <= 1; sc += 2)
                        {
                            var points = new Vector3[4];
                            var normal = Vector3.zero;
                            normal[a] = sa;
                            normal[c] = sc;
                            for (int i = 0; i < 4; i++)
                            {
                                bool outerA = i == 0 || i == 3;
                                points[i][axis] = (i < 2 ? 1f : -1f) * (half[axis] - b);
                                points[i][a] = sa * (half[a] - (outerA ? 0f : b));
                                points[i][c] = sc * (half[c] - (outerA ? b : 0f));
                            }

                            Solid.Face(points, normal, finish, Color.white, position, q);
                        }
                    }
                }

                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            var signs = new Vector3(x, y, z);
                            var corner = Vector3.Scale(half - Vector3.one * b, signs);
                            Solid.Face(new[] { corner + Vector3.right * (x * b), corner + Vector3.up * (y * b), corner + Vector3.forward * (z * b) },
                                signs, finish, Color.white, position, q);
                        }
                    }
                }
            }

            /// <summary>Low-sided, capped mechanical barrel along local +Z; radii may differ for taper.</summary>
            internal void Barrel(Vector3 position, float rearRadius, float frontRadius, float length, Finish finish, int sides = 8, Quaternion? rotation = null)
            {
                var q = rotation ?? Quaternion.identity;
                var rear = new Vector3[sides];
                var front = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float angle = (i + 0.5f) * Mathf.PI * 2f / sides;
                    var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    rear[i] = radial * rearRadius + Vector3.back * (length * 0.5f);
                    front[i] = radial * frontRadius + Vector3.forward * (length * 0.5f);
                }

                Solid.Face(rear, Vector3.back, finish, Color.white, position, q);
                Solid.Face(front, Vector3.forward, finish, Color.white, position, q);
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    var normal = new Vector3(rear[i].x + rear[next].x, rear[i].y + rear[next].y, 0f).normalized;
                    normal.z = (rearRadius - frontRadius) / Mathf.Max(length, 0.001f);
                    Solid.Face(new[] { rear[i], rear[next], front[next], front[i] }, normal, finish, Color.white, position, q);
                }
            }

            /// <summary>A single luminous inset, with dark housing behind it. No extra point light.</summary>
            internal void Signal(Vector3 position, Vector2 size, Color color, Quaternion? rotation = null)
            {
                var half = size * 0.5f;
                Signals.Face(new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f),
                    new Vector3(half.x, half.y, 0f), new Vector3(-half.x, half.y, 0f) },
                    Vector3.back, Finish.Glass, ShaderColor.Srgb(color), position, rotation ?? Quaternion.identity);
            }

            internal void SignalLine(Vector3 from, Vector3 to, float width, Color color)
            {
                Vector3 delta = to - from;
                Signal((from + to) * 0.5f, new Vector2(width, delta.magnitude), color,
                    Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg));
            }
        }
    }

    [ExecuteAlways]
    internal sealed class EquipmentGeometryLease : MonoBehaviour
    {
        internal string Key;
        internal Object OwnedResource;

        private void OnDestroy()
        {
            EquipmentGeometry.Release(Key);
            EquipmentGeometry.DestroyResource(OwnedResource);
        }
    }
}
