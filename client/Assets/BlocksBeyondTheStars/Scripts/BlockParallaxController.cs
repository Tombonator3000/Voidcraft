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
    /// The parallax shader uses the normal atlas' existing cavity channel as a conservative pseudo-height field.
    /// That means this first pass costs no extra texture memory and cannot change collision/silhouettes. A true
    /// authored height atlas can replace that source later without changing the quality/preset plumbing here.
    /// </summary>
    public sealed class BlockParallaxController : MonoBehaviour
    {
        private static readonly int ScaleId = Shader.PropertyToID("_ParallaxScale");
        private static readonly int DistanceId = Shader.PropertyToID("_ParallaxDistance");
        private static readonly int QualityId = Shader.PropertyToID("_ParallaxQuality");

        private static BlockParallaxController _instance;

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
                _game = FindFirstObjectByType<GameBootstrap>();
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
                        // catch while walking without turning the pixel-art blocks into wobbly relief maps.
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
