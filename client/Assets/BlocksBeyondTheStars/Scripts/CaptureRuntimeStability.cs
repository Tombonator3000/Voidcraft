// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Keeps automated screenshot runs advancing on virtual X11 displays.
    /// Xvfb can report a 0 Hz refresh rate; leaving VSync enabled in that
    /// environment can stall Unity's player loop before ScreenshotDirector
    /// reaches its first capture.
    /// </summary>
    internal static class CaptureRuntimeStability
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureCaptureRuntime()
        {
            var args = Environment.GetCommandLineArgs();
            bool capture = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-captureShots", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "-captureCredits", StringComparison.OrdinalIgnoreCase))
                {
                    capture = true;
                    break;
                }
            }

            if (!capture)
            {
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Debug.Log("[Capture] Virtual-display stability: VSync disabled, targetFrameRate=60.");
        }
    }
}
