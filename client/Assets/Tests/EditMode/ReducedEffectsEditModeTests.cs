// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class ReducedEffectsEditModeTests
    {
        [Test]
        public void AmbientParticles_LateSettingUpdatesTheCachedEmissionRate()
        {
            var root = new GameObject("Reduced effects ambient fixture");
            try
            {
                var ambient = root.AddComponent<AmbientParticles>();
                var baseRate = typeof(AmbientParticles).GetField("_baseRate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(baseRate);

                ambient.ReducedEffects = true;
                Assert.AreEqual(5f, (float)baseRate.GetValue(ambient));

                ambient.ReducedEffects = false;
                Assert.AreEqual(15f, (float)baseRate.GetValue(ambient));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
