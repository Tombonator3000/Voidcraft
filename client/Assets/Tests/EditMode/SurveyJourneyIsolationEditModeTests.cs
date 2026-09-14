// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class SurveyJourneyIsolationEditModeTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void QuitRestoresTheOriginalNativeUiModuleStateAndReleasesOnlyTheJourneyLease(bool originallyEnabled)
        {
            bool background = Application.runInBackground;
            var previousSystem = EventSystem.current;
            var root = new GameObject("Journey isolation fixture");
            var system = root.AddComponent<EventSystem>();
            // EventSystem.current can only reorder registered systems; it cannot register one.
            // EditMode does not invoke this component's OnEnable, so run that lifecycle registration
            // explicitly when absent, and balance it in cleanup. The actual isolation code is unchanged.
            var systems = (IList)typeof(EventSystem).GetField("m_EventSystems", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            bool registeredByFixture = !systems.Contains(system);
            if (registeredByFixture)
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
            EventSystem.current = system;
            Assert.AreSame(system, EventSystem.current, "The fixture must first register a real current EventSystem.");
            var module = root.AddComponent<StandaloneInputModule>();
            module.enabled = originallyEnabled;
            var probe = root.AddComponent<SurveyJourneyProbe>();
            var type = typeof(SurveyJourneyProbe);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var source = (JourneyInputSource)type.GetField("_input", fields).GetValue(probe);
            type.GetField("_previousRunInBackground", fields).SetValue(probe, background);
            try
            {
                Assert.IsTrue(InputMap.AttachVerificationInput(source));
                type.GetField("_attached", fields).SetValue(probe, true);
                type.GetMethod("SuspendNativeUiInput", fields).Invoke(probe, null);
                Assert.IsFalse(module.enabled, "Native pointer events cannot race the hit-tested journey click.");
                Assert.AreSame(system, EventSystem.current, "The real UI event system remains available for raycasts and pointer events.");
                type.GetMethod("OnApplicationQuit", fields).Invoke(probe, null);
                Assert.AreEqual(originallyEnabled, module.enabled);
                Assert.IsFalse(InputMap.OwnsVerificationInput(source));
            }
            finally
            {
                InputMap.DetachVerificationInput(source);
                if (registeredByFixture)
                    typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
                Object.DestroyImmediate(root);
                if (previousSystem != null) EventSystem.current = previousSystem;
                Application.runInBackground = background;
            }
        }
    }
}
