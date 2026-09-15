// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Server → current-world clients: a one-shot spatial acknowledgment of the first shared Veyl
/// repair. Not a snapshot, grant, or client intent. Existing contact glow and milestones persist instead.
/// Unknown message tags are ignored by older clients, so this cosmetic addition retains protocol v3.</summary>
public sealed class VeylSignalResponse
{
    public const int MaximumNodes = 24;
    public const float DurationSeconds = 2.4f;
    public string EventId { get; set; } = string.Empty;
    public VeylSignalNode[] Nodes { get; set; } = System.Array.Empty<VeylSignalNode>();
}

/// <summary>One surviving full-cube rune's exposed north (-Z) face, in canonical world coordinates.
/// Nodes arrive in pulse order; clients also require the host and adjacent air to be loaded.</summary>
public sealed class VeylSignalNode
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
