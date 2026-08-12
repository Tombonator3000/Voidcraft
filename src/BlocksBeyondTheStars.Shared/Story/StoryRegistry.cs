// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// Built-in fallbacks for the installed story packs. Runtime content is loaded from
/// <c>data/stories/&lt;id&gt;/</c>; these definitions keep a fresh world playable when that data is unavailable.
/// </summary>
public static class StoryRegistry
{
    /// <summary>The default/active pack id for a fresh world.</summary>
    public const string DefaultStoryId = "voidcraft_awakening";

    /// <summary>The original Blocks Beyond the Stars campaign, retained as an optional pack.</summary>
    public const string VegaProtocolStoryId = "vega_protocol";

    /// <summary>The reserved id that disables the story entirely (pure sandbox).</summary>
    public const string NoneStoryId = "none";

    private static readonly Dictionary<string, StoryDefinition> Packs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultStoryId] = BuildVoidcraftAwakening(),
            [VegaProtocolStoryId] = BuildVegaProtocol(),
        };

    /// <summary>All installed packs (excludes the "none" sentinel).</summary>
    public static IReadOnlyCollection<StoryDefinition> All => Packs.Values;

    /// <summary>The default pack for a fresh Voidcraft world.</summary>
    public static StoryDefinition Default => Packs[DefaultStoryId];

    /// <summary>True if the id is a real installed pack (not empty and not the "none" sentinel).</summary>
    public static bool IsStoryActive(string? storyId)
        => !string.IsNullOrEmpty(storyId)
           && !string.Equals(storyId, NoneStoryId, StringComparison.OrdinalIgnoreCase)
           && Packs.ContainsKey(storyId!);

    /// <summary>Looks up a pack by id. Returns false for empty/unknown ids and for the "none" sentinel.</summary>
    public static bool TryGet(string? storyId, out StoryDefinition definition)
    {
        if (!string.IsNullOrEmpty(storyId) && Packs.TryGetValue(storyId!, out var def))
        {
            definition = def;
            return true;
        }

        definition = Default;
        return false;
    }

    private static StoryDefinition BuildVoidcraftAwakening() => new()
    {
        Id = DefaultStoryId,
        NameKey = "story.voidcraft_awakening.name",
        FragmentWeight = 3,
        KillWeight = 1,
        MilestoneWeight = 2,
        KillContributionCap = 40,
        FinaleRevealTextKey = "story.voidcraft.guardian_revealed",
        FinaleResolvedTextKey = "story.voidcraft.finale_resolved",
        FinaleSystemNameKey = "story.voidcraft.guardian_system",
        InsightUnlockBeatCount = 6,
        CompanionWardTextKey = "story.voidcraft.insight.companion_ward",
        ShapeAnomalyTextKey = "story.voidcraft.insight.shape_anomaly",
        Beats = new List<StoryBeat>
        {
            VoidcraftBeat(0,  "No sky in memory",       0,   0),
            VoidcraftBeat(1,  "The signal below",       3,   1),
            VoidcraftBeat(2,  "The first seal",         8,   1),
            VoidcraftBeat(3,  "The same hand",          15,  1),
            VoidcraftBeat(4,  "World-shells",            24,  1),
            VoidcraftBeat(5,  "The Veyl",                36,  1),
            VoidcraftBeat(6,  "The Sleeper signal",      50,  1),
            VoidcraftBeat(7,  "The silence",             66,  1),
            VoidcraftBeat(8,  "The waking lattice",      84,  1),
            VoidcraftBeat(9,  "What you are",            104, 1),
            VoidcraftBeat(10, "Not the first",           126, 1),
            VoidcraftBeat(11, "The Black Anchor",        150, 1),
            VoidcraftBeat(12, "The choice",              176, 1),
        },
    };

    private static StoryDefinition BuildVegaProtocol() => new()
    {
        Id = VegaProtocolStoryId,
        NameKey = "story.vega_protocol.name",
        // progress = fragments*3 + min(kills,40)*1 + milestones*2
        FragmentWeight = 3,
        KillWeight = 1,
        MilestoneWeight = 2,
        KillContributionCap = 40,
        FinaleRevealTextKey = "story.vega.guardian_revealed",
        FinaleResolvedTextKey = "story.vega.finale_resolved",
        FinaleSystemNameKey = "story.vega.guardian_system",
        InsightUnlockBeatCount = 6,
        CompanionWardTextKey = "story.vega.insight.companion_ward",
        ShapeAnomalyTextKey = "story.vega.insight.shape_anomaly",
        // Beats pay +1 knowledge each (was +3): the arc alone used to out-earn the whole research
        // ladder's knowledge ceiling, trivialising the blueprint gate (#767).
        Beats = new List<StoryBeat>
        {
            VegaBeat(0,  "Systems online",        0,   0),
            VegaBeat(1,  "A familiar signature",  6,   1),
            VegaBeat(2,  "The Service",           14,  1),
            VegaBeat(3,  "Not scattered — erased", 24, 1),
            VegaBeat(4,  "Ours, once",            36,  1),
            VegaBeat(5,  "The Guardian",          50,  1),
            VegaBeat(6,  "The verdict",           66,  1),
            VegaBeat(7,  "Her stand",             84,  1),
            VegaBeat(8,  "The thought-arcs",      104, 1),
            VegaBeat(9,  "What you are",          126, 1),  // the clone reveal
            VegaBeat(10, "Many minds",            150, 1),
            VegaBeat(11, "It still sleeps",       176, 1),  // locates the dormant Guardian system
            VegaBeat(12, "The choice",            204, 1),  // finale opens
        },
    };

    private static StoryBeat VoidcraftBeat(int index, string title, int threshold, int knowledge)
        => Beat(index, title, "story.voidcraft.beat", threshold, knowledge);

    private static StoryBeat VegaBeat(int index, string title, int threshold, int knowledge)
        => Beat(index, title, "story.vega.beat", threshold, knowledge);

    private static StoryBeat Beat(int index, string title, string textKeyPrefix, int threshold, int knowledge) => new()
    {
        Index = index,
        Title = title,
        TextKey = textKeyPrefix + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
        Threshold = threshold,
        KnowledgeReward = knowledge,
    };
}
