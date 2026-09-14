// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Opt-in verification input. A publication lasts one frame, even for held buttons. Reads
    /// never consume edges, so every ordinary input consumer observes the same frame snapshot.</summary>
    public sealed class JourneyInputSource : IInputSource
    {
        public struct Frame
        {
            public Vector2 Move, Look;
            public float Scroll;
            public int SlotOneBased; // zero = no direct pick, 1..9 = a hotbar key
            public bool JumpHeld, JumpDown, CrouchHeld, PrimaryDown, PrimaryHeld, SecondaryDown;
            public ulong ActionsDown, ActionsHeld, ActionsUp;

            public void Press(InputAction action)
            {
                ActionsDown |= Mask(action);
                ActionsHeld |= Mask(action);
            }
        }

        private readonly Func<int> _frameNumber;
        private int _publishedFrame = -1;
        private Frame _frame;
        public JourneyInputSource(Func<int> frameNumber = null) => _frameNumber = frameNumber ?? (() => Time.frameCount);
        public void Publish(Frame frame) { _frame = frame; _publishedFrame = _frameNumber(); }
        public void Clear() { _frame = default; _publishedFrame = -1; }
        private Frame Current => _publishedFrame == _frameNumber() ? _frame : default;
        private static ulong Mask(InputAction action) => (uint)action < 64 ? 1UL << (int)action : 0;
        public InputDeviceKind Kind => InputDeviceKind.KeyboardMouse;
        public float MoveX() => Current.Move.x;
        public float MoveY() => Current.Move.y;
        public float LookX() => Current.Look.x;
        public float LookY() => Current.Look.y;
        public float HotbarScroll() => Current.Scroll;
        public int HotbarSlotDown() => Current.SlotOneBased is >= 1 and <= 9 ? Current.SlotOneBased - 1 : -1;
        public bool JumpHeld() => Current.JumpHeld;
        public bool JumpDown() => Current.JumpDown;
        public bool CrouchHeld() => Current.CrouchHeld;
        public bool PrimaryDown() => Current.PrimaryDown;
        public bool PrimaryHeld() => Current.PrimaryHeld;
        public bool SecondaryDown() => Current.SecondaryDown;
        public bool ActionDown(InputAction action) => (Current.ActionsDown & Mask(action)) != 0;
        public bool ActionHeld(InputAction action) => (Current.ActionsHeld & Mask(action)) != 0;
        public bool ActionUp(InputAction action) => (Current.ActionsUp & Mask(action)) != 0;
        public bool HadActivityThisFrame()
        {
            var f = Current;
            return f.Move != Vector2.zero || f.Look != Vector2.zero || f.Scroll != 0f || f.SlotOneBased != 0
                || f.JumpHeld || f.JumpDown || f.CrouchHeld || f.PrimaryDown || f.PrimaryHeld || f.SecondaryDown
                || f.ActionsDown != 0 || f.ActionsHeld != 0 || f.ActionsUp != 0;
        }
    }
}
