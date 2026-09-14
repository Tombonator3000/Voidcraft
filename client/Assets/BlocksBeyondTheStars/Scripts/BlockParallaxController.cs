// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Selects the optional URP parallax variant for opaque voxel terrain without changing world data, meshing
    /// or the normal atlas. Medium gets a deliberately subtle/near effect; High gets the full short-range pass.
    /// Potato/Low, reduced-effects mode and Built-in RP keep the shipping <c>BlockAtlas</c> shader unchanged.
    ///
    /// Authored materials sample dedicated physical height from the packed surface atlas. Legacy tiles
    /// retain the cavity fallback. This remains a presentation effect and never changes collision or silhouettes.
    /// </summary>
    public sealed class BlockParallaxController : MonoBehaviour
    {
        private static readonly int ScaleId = Shader.PropertyToID("_ParallaxScale");
        private static readonly int DistanceId = Shader.PropertyToID("_ParallaxDistance");
        private static readonly int QualityId = Shader.PropertyToID("_ParallaxQuality");

        private static BlockParallaxController _instance;
        private static IInputSource _performanceOwner;
        private static bool _performanceParallaxEnabled = true;

        /// <summary>Keep the preset shader and lighting while isolating only its parallax sampling cost.
        /// Available solely to the exclusive owner of an explicit performance run.</summary>
        public static bool TryOverrideForPerformance(IInputSource owner, bool enabled, out float appliedScale)
        {
            appliedScale = -1f;
            bool allowed = Application.isEditor || System.Array.Exists(System.Environment.GetCommandLineArgs(),
                arg => string.Equals(arg, "-perfProbe", System.StringComparison.OrdinalIgnoreCase));
            var controller = _instance;
            var settings = controller?._game?.Settings;
            if (!allowed || !InputMap.OwnsVerificationInput(owner)
                || (_performanceOwner != null && !object.ReferenceEquals(_performanceOwner, owner))
                || controller == null || controller._material == null || controller._parallaxShader == null
                || settings == null || settings.ReducedEffects || settings.Preset < QualityPreset.Medium
                || UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                return false;
            _performanceOwner = owner;
            _performanceParallaxEnabled = enabled;
            controller.ApplyPreset();
            appliedScale = controller._material.GetFloat(ScaleId);
            return controller._material.shader == controller._parallaxShader
                && (enabled ? appliedScale > 0f : appliedScale == 0f);
        }

        public static void ReleasePerformanceOverride(IInputSource owner)
        {
            if (!object.ReferenceEquals(_performanceOwner, owner)) return;
            _performanceOwner = null;
            _performanceParallaxEnabled = true;
            _instance?.ApplyPreset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPerformanceOverride()
        {
            _performanceOwner = null;
            _performanceParallaxEnabled = true;
        }

        private GameBootstrap _game;
        private Material _material;
        private Shader _baseShader;
        private Shader _parallaxShader;
        private float _nextProbe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject("BlockParallaxController");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<BlockParallaxController>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextProbe)
            {
                return;
            }

            _nextProbe = Time.unscaledTime + 0.25f;

            if (_game == null)
            {
                _game = FindAnyObjectByType<GameBootstrap>();
            }

            if (_game == null || _game.ChunkMaterial == null)
            {
                Restore();
                _material = null;
                return;
            }

            if (_baseShader == null)
            {
                _baseShader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            }

            if (_parallaxShader == null)
            {
                _parallaxShader = Resources.Load<Shader>("BlockAtlasParallax")
                    ?? Shader.Find("BlocksBeyondTheStars/BlockAtlasParallax");
            }

            if (_material != _game.ChunkMaterial)
            {
                Restore();
                _material = _game.ChunkMaterial;
            }

            ApplyPreset();
        }

        private void ApplyPreset()
        {
            if (_material == null || _baseShader == null)
            {
                return;
            }

            var settings = _game?.Settings;
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            bool reduced = settings == null || settings.ReducedEffects;

            float scale = 0f;
            float distance = 0f;
            float quality = 0f;
            bool enabled = false;

            if (urp && !reduced && _parallaxShader != null && settings != null)
            {
                switch (settings.Preset)
                {
                    case QualityPreset.Medium:
                        // Four layers, small displacement, near the camera only. Enough to make cracks/panels
                        // catch while walking without making the broad panels appear to float.
                        enabled = true;
                        scale = 0.010f;
                        distance = 10f;
                        quality = 0f;
                        break;

                    case QualityPreset.High:
                        // Eight layers and a little more depth, still bounded to the near field where the effect
                        // is visible. Beyond this distance normal mapping carries the material on its own.
                        enabled = true;
                        scale = 0.022f;
                        distance = 22f;
                        quality = 1f;
                        break;
                }
            }

            Shader wanted = enabled ? _parallaxShader : _baseShader;
            if (_material.shader != wanted)
            {
                _material.shader = wanted;
            }

            if (enabled)
            {
                if (_performanceOwner != null && InputMap.OwnsVerificationInput(_performanceOwner)
                    && !_performanceParallaxEnabled) scale = 0f;
                _material.SetFloat(ScaleId, scale);
                _material.SetFloat(DistanceId, distance);
                _material.SetFloat(QualityId, quality);
            }
        }

        private void Restore()
        {
            if (_material != null && _baseShader != null && _material.shader != _baseShader)
            {
                _material.shader = _baseShader;
            }
        }

        private void OnDestroy()
        {
            Restore();
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
