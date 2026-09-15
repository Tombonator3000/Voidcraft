// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    internal static class ScanRay
    {
        // Surface entry, not centre-plane distance: a visible body in front of a wall remains aimed.
        public static bool SphereEntry(Ray ray, Vector3 center, float radius, out float distance)
        {
            distance = 0f;
            Vector3 direction = ray.direction.normalized;
            if (direction.sqrMagnitude < 0.99f || radius <= 0f) return false;
            Vector3 offset = center - ray.origin;
            float along = Vector3.Dot(offset, direction);
            float discriminant = radius * radius - (offset.sqrMagnitude - along * along);
            if (discriminant < 0f) return false;
            float halfChord = Mathf.Sqrt(discriminant);
            if (along + halfChord < 0f) return false;
            distance = Mathf.Max(0f, along - halfChord);
            return true;
        }
    }
}
