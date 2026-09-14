// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class PerformanceInputIsolationEditModeTests
    {
        [Test]
        public void PerformanceLeaseExcludesLegacyMovement_AndOnlyItsOwnerCanReleaseIt()
        {
            var owner = new JourneyInputSource();
            var other = new JourneyInputSource();
            Vector2 previous = InputMap.ScriptedMove;
            try
            {
                InputMap.ScriptedMove = Vector2.one;
                Assert.IsTrue(InputMap.AttachPerformanceInput(owner));
                Assert.IsTrue(InputMap.OwnsVerificationInput(owner));
                Assert.IsFalse(InputMap.AttachVerificationInput(other));
                Assert.IsFalse(InputMap.AttachPerformanceInput(other));
                Assert.AreEqual(0f, InputMap.MoveX());
                Assert.AreEqual(0f, InputMap.MoveY(), "An idle lease must exclude the legacy additive walk input.");
                Assert.AreEqual(-1, InputMap.HotbarSlotDown());
                InputMap.DetachVerificationInput(other);
                Assert.IsTrue(InputMap.OwnsVerificationInput(owner));
                InputMap.DetachVerificationInput(owner);
                Assert.IsFalse(InputMap.OwnsVerificationInput(owner));
                Assert.IsTrue(InputMap.AttachVerificationInput(other), "Ordinary journey attachment must work after release.");
            }
            finally
            {
                InputMap.DetachVerificationInput(owner);
                InputMap.DetachVerificationInput(other);
                InputMap.ScriptedMove = previous;
            }
        }

        [Test]
        public void PerformanceLeasePublishesOnlyItsFrame_AndExpiredInputReturnsToIdle()
        {
            int frame = 30;
            var owner = new JourneyInputSource(() => frame);
            try
            {
                Assert.IsTrue(InputMap.AttachPerformanceInput(owner));
                var input = new JourneyInputSource.Frame
                {
                    Move = new Vector2(-0.25f, 0.75f), Look = new Vector2(2f, -3f), Scroll = -1f,
                    SlotOneBased = 4, JumpHeld = true, JumpDown = true, CrouchHeld = true,
                    PrimaryDown = true, PrimaryHeld = true, SecondaryDown = true,
                };
                input.Press(InputAction.Interact);
                owner.Publish(input);
                Assert.AreEqual(-0.25f, InputMap.MoveX());
                Assert.AreEqual(0.75f, InputMap.MoveY());
                Assert.AreEqual(2f, InputMap.LookX());
                Assert.AreEqual(-3f, InputMap.LookY());
                Assert.AreEqual(-1f, InputMap.HotbarScroll());
                Assert.AreEqual(3, InputMap.HotbarSlotDown());
                Assert.IsTrue(InputMap.JumpHeld() && InputMap.JumpDown() && InputMap.CrouchHeld());
                Assert.IsTrue(InputMap.PrimaryDown() && InputMap.PrimaryHeld() && InputMap.SecondaryDown());
                Assert.IsTrue(InputMap.Down(InputAction.Interact) && InputMap.Held(InputAction.Interact));
                frame++;
                Assert.AreEqual(0f, InputMap.MoveY());
                Assert.AreEqual(0f, InputMap.LookX());
                Assert.AreEqual(-1, InputMap.HotbarSlotDown());
                Assert.IsFalse(InputMap.JumpHeld() || InputMap.JumpDown() || InputMap.PrimaryHeld());
                Assert.IsFalse(InputMap.Down(InputAction.Interact) || InputMap.Held(InputAction.Interact));
            }
            finally { InputMap.DetachVerificationInput(owner); }
        }
    }
}
