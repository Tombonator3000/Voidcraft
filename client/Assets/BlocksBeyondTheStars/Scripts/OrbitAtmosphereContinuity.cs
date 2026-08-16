// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Presentation-only bridge between the surface environment and the body the player just launched from.
    /// <para>
    /// <see cref="SpaceView"/> intentionally owns flight/landing and builds its scene procedurally. Rather than
    /// mixing atmosphere experiments into that large gameplay-facing class, this tiny runtime companion watches
    /// for its <c>HomePlanet</c> and layers the current authoritative <see cref="GameBootstrap.Environment"/>
    /// identity onto it: the same sky hue, atmosphere density, sun colour and cloud tint the player saw on the
    /// ground. The original lightweight orbit haze is temporarily disabled while this shell exists and restored
    /// if the companion detaches.
    /// </para>
    /// This is deliberately scoped to the active/home body. Other system bodies keep their deterministic
    /// type-based orbit look until their full environment payload is available client-side.
    /// </summary>
    public sealed class OrbitAtmosphereContinuity : MonoBehaviour
    {
        private static readonly int AtmosphereColorId = Shader.PropertyToID("_AtmosphereColor");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int WeatherId = Shader.PropertyToID("_Weather");

        private static readonly int CloudColorId = Shader.PropertyToID("_Color");
        private static readonly int CloudShadeId = Shader.PropertyToID("_ShadeColor");
        private static readonly int CloudSunDirId = Shader.PropertyToID("_CloudSunDir");

        private static OrbitAtmosphereContinuity _instance;

        private GameBootstrap _game;
        private SpaceView _space;
        private Transform _home;
        private GameObject _shell;
        private Material _material;
        private Renderer _legacyAtmosphere;
        private bool _legacyWasEnabled;
        private float _nextProbe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject("OrbitAtmosphereContinuity");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<OrbitAtmosphereContinuity>();
        }

        private void Update()
        {
            // The space scene is rebuilt only on transitions; probing at 5 Hz is plenty and avoids a global
            // object-name lookup every rendered frame.
            if (Time.unscaledTime < _nextProbe)
            {
                return;
            }

            _nextProbe = Time.unscaledTime + 0.2f;

            // UnityEngine.Object has a special destroyed-object null state. Use Unity's overloaded null check
            // rather than ??= so a menu/world transition can reacquire components that Unity has destroyed.
            if (_game == null)
            {
                _game = FindFirstObjectByType<GameBootstrap>();
            }

            if (_space == null)
            {
                _space = FindFirstObjectByType<SpaceView>();
            }

            var env = _game?.Environment;
            if (_game == null || _space == null || !_game.SpaceViewActive || env == null || env.SpaceSky)
            {
                Detach();
                return;
            }

            var homeGo = GameObject.Find("HomePlanet");
            if (homeGo == null)
            {
                Detach();
                return;
            }

            if (_home != homeGo.transform)
            {
                Detach();
                _home = homeGo.transform;
                Attach();
            }

            if (_shell == null || _material == null)
            {
                Attach();
            }

            if (_shell == null || _material == null)
            {
                return;
            }

            float density = Mathf.Clamp01(env.AtmosphereDensity);
            float weather = Mathf.Clamp01(env.Intensity);
            Color atmosphere = Rgb(env.SkyColor);
            Color sun = Rgb(env.SunColor);
            Vector3 sunDir = ResolveSunDirection();

            // A denser atmosphere reads as a visibly thicker limb from orbit; still close enough to the body
            // that the shell cannot look like a second planet.
            float shellScale = Mathf.Lerp(1.045f, 1.095f, density);
            _shell.transform.localScale = Vector3.one * shellScale;

            _material.SetColor(AtmosphereColorId, ShaderColor.Srgb(atmosphere));
            _material.SetColor(SunColorId, ShaderColor.Srgb(sun));
            _material.SetVector(SunDirId, sunDir);
            _material.SetFloat(DensityId, density);
            _material.SetFloat(WeatherId, weather);

            SyncCloudShell(env.CloudColor, env.CloudDensity, sunDir);
        }

        private void Attach()
        {
            if (_home == null || _shell != null)
            {
                return;
            }

            var shader = Resources.Load<Shader>("OrbitalAtmosphere");
            if (shader == null)
            {
                // Resource loading should make the build retain it; Shader.Find is a useful editor/dev fallback.
                shader = Shader.Find("BlocksBeyondTheStars/OrbitalAtmosphere");
            }

            if (shader == null)
            {
                return;
            }

            var legacy = _home.Find("Atmosphere");
            _legacyAtmosphere = legacy != null ? legacy.GetComponent<Renderer>() : null;
            if (_legacyAtmosphere != null)
            {
                _legacyWasEnabled = _legacyAtmosphere.enabled;
                _legacyAtmosphere.enabled = false;
            }

            _shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _shell.name = "AtmosphereContinuity";
            var collider = _shell.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            _shell.transform.SetParent(_home, false);
            _shell.transform.localPosition = Vector3.zero;
            _shell.transform.localRotation = Quaternion.identity;

            _material = new Material(shader) { renderQueue = 2999 };
            var renderer = _shell.GetComponent<Renderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private Vector3 ResolveSunDirection()
        {
            if (_space != null && _space.HasStar && _home != null)
            {
                Vector3 local = _space.StarPosition - _home.localPosition;
                if (local.sqrMagnitude > 0.0001f)
                {
                    local.Normalize();
                    return _home.parent != null ? _home.parent.TransformDirection(local).normalized : local;
                }
            }

            // No star-map data: match SpaceView's deliberately stylised fallback direction closely enough that
            // the planet phase and atmosphere limb still agree visually.
            return new Vector3(0.35f, 0.62f, -0.70f).normalized;
        }

        private void SyncCloudShell(int rgb, float density, Vector3 sunDir)
        {
            if (_home == null)
            {
                return;
            }

            var cloud = _home.Find("CloudShell");
            var renderer = cloud != null ? cloud.GetComponent<Renderer>() : null;
            var mat = renderer != null ? renderer.sharedMaterial : null;
            if (mat == null)
            {
                return;
            }

            Color c = Rgb(rgb);
            c.a = Mathf.Clamp01(0.55f + Mathf.Clamp01(density) * 0.4f);
            if (mat.HasProperty(CloudColorId))
            {
                mat.SetColor(CloudColorId, ShaderColor.Srgb(c));
            }

            if (mat.HasProperty(CloudShadeId))
            {
                Color shade = c * 0.25f;
                shade.a = c.a;
                mat.SetColor(CloudShadeId, ShaderColor.Srgb(shade));
            }

            if (mat.HasProperty(CloudSunDirId))
            {
                mat.SetVector(CloudSunDirId, sunDir);
            }
        }

        private void Detach()
        {
            if (_legacyAtmosphere != null)
            {
                _legacyAtmosphere.enabled = _legacyWasEnabled;
            }

            _legacyAtmosphere = null;
            if (_shell != null)
            {
                Destroy(_shell);
            }

            if (_material != null)
            {
                Destroy(_material);
            }

            _shell = null;
            _material = null;
            _home = null;
        }

        private void OnDestroy()
        {
            Detach();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
