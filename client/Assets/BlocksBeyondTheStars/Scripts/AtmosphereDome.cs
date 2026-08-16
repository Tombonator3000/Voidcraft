// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Camera-centred atmospheric scattering dome (<c>BlocksBeyondTheStars/Atmosphere</c>). It complements
    /// <see cref="Sky"/>'s day/night sky colour with a cheap, stylised Rayleigh/Mie-inspired scattering layer:
    /// longer optical paths brighten the horizon, forward Mie scattering builds the sun halo, and low-sun
    /// extinction warms dawn/dusk. Planet atmosphere density and weather intensity drive the shader so different
    /// worlds do not share one fixed Earth-like look.
    ///
    /// The dome follows the camera at "infinity" like <see cref="Starfield"/> / <see cref="NebulaField"/> and fades
    /// out in space, on airless bodies and inside stations. It is presentation-only; no world/gameplay state is
    /// derived here.
    /// </summary>
    public sealed class AtmosphereDome : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int WeatherId = Shader.PropertyToID("_Weather");

        private Transform _dome;
        private Material _mat;
        private Mesh _mesh;
        private float _brightness; // smoothed 0..1 visibility fade
        private float _density = 0.4f;
        private float _weather;

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Atmosphere");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader);
            _mat.SetFloat(BrightnessId, 0f);
            _mat.SetFloat(DensityId, _density);
            _mat.SetFloat(WeatherId, 0f);

            var go = new GameObject("Atmosphere");
            go.transform.SetParent(transform, false);
            _mesh = BuildDomeMesh();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _dome = go.transform;
        }

        private void LateUpdate()
        {
            if (_dome == null || Camera == null || Game == null)
            {
                return;
            }

            _dome.SetPositionAndRotation(Camera.transform.position, Quaternion.identity);
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.43f; // just inside the star/nebula domes
            _dome.localScale = new Vector3(r, r, r);

            var env = Game.Environment;

            // Visible only where there IS an atmosphere: a normal planet sky. Off in the space view, on airless
            // bodies and inside an orbital station (those show the nebula/stars instead).
            bool spaceSky = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                            || (env != null && env.SpaceSky) || Game.OnFootInSpace;
            float targetBrightness = spaceSky ? 0f : 1f;
            _brightness = Mathf.MoveTowards(_brightness, targetBrightness, Time.deltaTime * 0.9f);

            // These are server-seeded/world-authoritative environment values, consumed only as visual controls.
            // Smooth them because environment snapshots/weather transitions can arrive discretely.
            float targetDensity = env != null ? Mathf.Clamp01(env.AtmosphereDensity) : 0.4f;
            float targetWeather = env != null ? Mathf.Clamp01(env.Intensity) : 0f;
            _density = Mathf.MoveTowards(_density, targetDensity, Time.deltaTime * 0.6f);
            _weather = Mathf.MoveTowards(_weather, targetWeather, Time.deltaTime * 0.7f);

            _mat.SetFloat(BrightnessId, _brightness);
            _mat.SetFloat(DensityId, _density);
            _mat.SetFloat(WeatherId, _weather);
        }

        private void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        /// <summary>A unit UV-sphere dome (positions only — the shader derives the view direction from them).</summary>
        private static Mesh BuildDomeMesh()
        {
            const int rings = 20;
            const int sectors = 40;
            int vCount = (rings + 1) * (sectors + 1);
            var verts = new Vector3[vCount];
            var tris = new int[rings * sectors * 6];

            int vi = 0;
            for (int ring = 0; ring <= rings; ring++)
            {
                float theta = (float)ring / rings * Mathf.PI;
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int sec = 0; sec <= sectors; sec++)
                {
                    float phi = (float)sec / sectors * Mathf.PI * 2f;
                    verts[vi++] = new Vector3(sinT * Mathf.Cos(phi), cosT, sinT * Mathf.Sin(phi));
                }
            }

            int ti = 0, stride = sectors + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int sec = 0; sec < sectors; sec++)
                {
                    int a = ring * stride + sec;
                    int b = a + stride;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }
    }
}
