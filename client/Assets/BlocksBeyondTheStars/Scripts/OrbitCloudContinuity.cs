// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Keeps the launch body's orbital cloud COVER consistent with the surface weather. SpaceView already owns
    /// the cheap rotating cloud sphere; this companion only swaps its generated coverage mask. Colour, lighting
    /// and atmosphere identity stay with OrbitAtmosphereContinuity / SpaceView.
    ///
    /// The pattern seed is stable for the current location, so weather changes expand/contract the same cloud
    /// masses instead of replacing the whole sky. Presentation only; no gameplay or server state is changed.
    /// </summary>
    public sealed class OrbitCloudContinuity : MonoBehaviour
    {
        private static OrbitCloudContinuity _instance;

        private GameBootstrap _game;
        private Transform _home;
        private Renderer _cloudRenderer;
        private Material _cloudMaterial;
        private Texture _originalTexture;
        private Texture2D _generated;
        private float _lastCoverage = -1f;
        private string _lastLocation = string.Empty;
        private float _nextProbe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("OrbitCloudContinuity");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<OrbitCloudContinuity>();
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

            var env = _game?.Environment;
            if (_game == null || !_game.SpaceViewActive || env == null || env.SpaceSky)
            {
                Detach();
                return;
            }

            var homeGo = GameObject.Find("HomePlanet");
            Transform home = homeGo != null ? homeGo.transform : null;
            Transform shell = home != null ? home.Find("CloudShell") : null;
            Renderer renderer = shell != null ? shell.GetComponent<Renderer>() : null;
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (home == null || renderer == null || material == null)
            {
                Detach();
                return;
            }

            if (_home != home || _cloudMaterial != material)
            {
                Detach();
                _home = home;
                _cloudRenderer = renderer;
                _cloudMaterial = material;
                _originalTexture = material.mainTexture;
            }

            float coverage = EffectiveCoverage(env.Weather, env.CloudDensity);
            string location = _game.LocationName ?? string.Empty;
            if (_generated == null || Mathf.Abs(coverage - _lastCoverage) >= 0.035f || location != _lastLocation)
            {
                RebuildMask(coverage, StableSeed(location));
                _lastCoverage = coverage;
                _lastLocation = location;
            }
        }

        /// <summary>Mirrors the surface Clouds weather-cover targets so orbit and ground tell the same story.</summary>
        private static float EffectiveCoverage(string weather, float baseDensity)
        {
            float weatherCover = weather switch
            {
                "storm" => 0.95f,
                "blizzard" => 0.98f,
                "ember_fall" => 0.92f,
                "acid_rain" => 0.88f,
                "ion_storm" => 0.55f,
                "rain" => 0.80f,
                "gale" => 0.55f,
                "drizzle" => 0.70f,
                "fog" => 0.72f,
                "ground_fog" => 0.72f,
                "clouds" => 0.60f,
                "heatwave" => Mathf.Clamp01(baseDensity) * 0.20f,
                _ => Mathf.Clamp01(baseDensity) * 0.50f,
            };

            return Mathf.Clamp01(Mathf.Max(baseDensity, weatherCover));
        }

        private void RebuildMask(float coverage, int seed)
        {
            if (_cloudMaterial == null)
            {
                return;
            }

            if (_generated != null)
            {
                Destroy(_generated);
            }

            const int width = 128;
            const int height = 64;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true)
            {
                name = "OrbitCloudCoverage",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            float threshold = Mathf.Lerp(0.74f, 0.27f, coverage);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)height;
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)width;
                    float f = TiledFbm(u, v, seed);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - threshold) * 3.8f));
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            _generated = tex;
            _cloudMaterial.mainTexture = tex;
        }

        private void Detach()
        {
            if (_cloudMaterial != null && _originalTexture != null)
            {
                _cloudMaterial.mainTexture = _originalTexture;
            }

            if (_generated != null)
            {
                Destroy(_generated);
            }

            _generated = null;
            _originalTexture = null;
            _cloudMaterial = null;
            _cloudRenderer = null;
            _home = null;
            _lastCoverage = -1f;
            _lastLocation = string.Empty;
        }

        private void OnDestroy()
        {
            Detach();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private static int StableSeed(string text)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in text ?? string.Empty)
                {
                    h = h * 31 + c;
                }

                return h;
            }
        }

        private static float TiledFbm(float u, float v, int seed)
        {
            float sum = 0f;
            float amp = 0.55f;
            int frequency = 3;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += amp * TiledNoise(u * frequency, v * frequency, frequency, seed + octave * 101);
                amp *= 0.5f;
                frequency *= 2;
            }

            return sum;
        }

        private static float TiledNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;
            float sx = xf * xf * (3f - 2f * xf);
            float sy = yf * yf * (3f - 2f * yf);

            int x0 = Mod(xi, period);
            int x1 = (x0 + 1) % period;
            int y0 = Mod(yi, period);
            int y1 = (y0 + 1) % period;

            float a = Mathf.Lerp(Hash01(x0, y0, seed), Hash01(x1, y0, seed), sx);
            float b = Mathf.Lerp(Hash01(x0, y1, seed), Hash01(x1, y1, seed), sx);
            return Mathf.Lerp(a, b, sy);
        }

        private static int Mod(int value, int modulus)
            => ((value % modulus) + modulus) % modulus;

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
