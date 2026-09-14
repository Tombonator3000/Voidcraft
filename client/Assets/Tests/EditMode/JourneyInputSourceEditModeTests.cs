// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    public sealed class JourneyInputSourceEditModeTests
    {
        [Test]
        public void RepeatedConsumersSeeTheSameEdge_AndMissedPublicationCannotLeaveInputHeld()
        {
            int frame = 10;
            var source = new JourneyInputSource(() => frame);
            var input = new JourneyInputSource.Frame { Move = Vector2.up, PrimaryDown = true, PrimaryHeld = true, SlotOneBased = 4 };
            input.Press(InputAction.HotbarAction);
            source.Publish(input);
            Assert.IsTrue(source.PrimaryDown());
            Assert.IsTrue(source.PrimaryDown(), "The first consumer must not steal the click from the controller.");
            Assert.IsTrue(source.ActionDown(InputAction.HotbarAction));
            Assert.IsTrue(source.ActionDown(InputAction.HotbarAction));
            Assert.AreEqual(3, source.HotbarSlotDown());
            Assert.AreEqual(1f, source.MoveY());
            frame++;
            Assert.IsFalse(source.PrimaryDown());
            Assert.IsFalse(source.PrimaryHeld(), "A crashed or stalled driver must not keep mining indefinitely.");
            Assert.IsFalse(source.ActionHeld(InputAction.HotbarAction));
            Assert.AreEqual(-1, source.HotbarSlotDown());
            Assert.AreEqual(0f, source.MoveY());
            Assert.IsFalse(source.HadActivityThisFrame());
        }

        [Test]
        public void VerificationAttachmentHasOneOwner_AndAnotherSourceCannotDetachIt()
        {
            var owner = new JourneyInputSource();
            var other = new JourneyInputSource();
            try
            {
                Assert.IsTrue(InputMap.AttachVerificationInput(owner));
                Assert.IsTrue(InputMap.OwnsVerificationInput(owner));
                Assert.IsFalse(InputMap.AttachPerformanceInput(other), "Journey and performance must not share input ownership.");
                Assert.IsFalse(InputMap.AttachVerificationInput(other));
                InputMap.DetachVerificationInput(other);
                Assert.IsFalse(InputMap.AttachVerificationInput(other), "A different source must not detach the active driver.");
                InputMap.DetachVerificationInput(owner);
                Assert.IsTrue(InputMap.AttachVerificationInput(other));
            }
            finally
            {
                InputMap.DetachVerificationInput(owner);
                InputMap.DetachVerificationInput(other);
            }
        }

        [Test]
        public void ClearImmediatelyReleasesAllControls_AndDefaultDoesNotSelectSlotZero()
        {
            int frame = 20;
            var source = new JourneyInputSource(() => frame);
            source.Publish(new JourneyInputSource.Frame { PrimaryHeld = true, JumpHeld = true, Look = Vector2.one });
            Assert.IsTrue(source.HadActivityThisFrame());
            source.Clear();
            Assert.IsFalse(source.HadActivityThisFrame());
            Assert.IsFalse(source.JumpHeld());
            Assert.AreEqual(0f, source.LookX());
            source.Publish(default);
            Assert.AreEqual(-1, source.HotbarSlotDown());
        }
    }
}
