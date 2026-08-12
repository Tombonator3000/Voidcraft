// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Story-pack format + loader: the Voidcraft default and the original VEGA Protocol both load from
/// `data/stories/`, every Voidcraft-owned line resolves bilingually, and its data matches the fallback.
/// </summary>
public class StoryPackLoadTests
{
    private static GameContent Load() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Loads_the_voidcraft_awakening_pack_from_data()
    {
        var content = Load();
        Assert.True(content.Stories.ContainsKey("voidcraft_awakening"));

        var def = content.Stories["voidcraft_awakening"];
        Assert.Equal("voidcraft_awakening", def.Id);
        Assert.Equal(13, def.Beats.Count);        // B0..B12
        Assert.Equal(0, def.Beats[0].Threshold);
        for (int i = 1; i < def.Beats.Count; i++)
        {
            Assert.Equal(i, def.Beats[i].Index);
            Assert.True(def.Beats[i].Threshold >= def.Beats[i - 1].Threshold);
        }
    }

    [Fact]
    public void Keeps_the_original_vega_protocol_pack_available()
    {
        var content = Load();
        Assert.True(content.Stories.ContainsKey("vega_protocol"));
        var def = content.Stories["vega_protocol"];
        Assert.Equal(13, def.Beats.Count);
        Assert.Equal("story.vega.guardian_revealed", def.FinaleRevealTextKey);
        Assert.Equal("story.vega.finale_resolved", def.FinaleResolvedTextKey);
    }

    [Fact]
    public void Data_pack_matches_the_builtin_fallback_no_drift()
    {
        var data = Load().Stories["voidcraft_awakening"];
        var builtin = StoryRegistry.Default;

        Assert.Equal(builtin.Beats.Count, data.Beats.Count);
        Assert.Equal(builtin.FragmentWeight, data.FragmentWeight);
        Assert.Equal(builtin.KillWeight, data.KillWeight);
        Assert.Equal(builtin.MilestoneWeight, data.MilestoneWeight);
        Assert.Equal(builtin.KillContributionCap, data.KillContributionCap);
        Assert.Equal(builtin.FinaleRevealTextKey, data.FinaleRevealTextKey);
        Assert.Equal(builtin.FinaleResolvedTextKey, data.FinaleResolvedTextKey);
        Assert.Equal(builtin.FinaleSystemNameKey, data.FinaleSystemNameKey);
        Assert.Equal(builtin.InsightUnlockBeatCount, data.InsightUnlockBeatCount);
        Assert.Equal(builtin.CompanionWardTextKey, data.CompanionWardTextKey);
        Assert.Equal(builtin.ShapeAnomalyTextKey, data.ShapeAnomalyTextKey);
        for (int i = 0; i < builtin.Beats.Count; i++)
        {
            Assert.Equal(builtin.Beats[i].Threshold, data.Beats[i].Threshold);
            Assert.Equal(builtin.Beats[i].TextKey, data.Beats[i].TextKey);
        }
    }

    [Fact]
    public void Every_voidcraft_story_text_resolves_in_both_languages()
    {
        var content = Load();
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);
        var def = content.Stories["voidcraft_awakening"];

        Assert.Equal("Voidcraft: The Sleeper Signal", en.Get(def.NameKey));
        Assert.Equal("Voidcraft: Das Signal des Schläfers", de.Get(def.NameKey));

        var keys = new List<string>
        {
            def.NameKey,
            def.FinaleRevealTextKey,
            def.FinaleResolvedTextKey,
            def.FinaleSystemNameKey,
            def.CompanionWardTextKey,
            def.ShapeAnomalyTextKey,
        };
        keys.AddRange(def.Beats.Select(x => x.TextKey));
        keys.AddRange(def.Fragments.SelectMany(x => new[] { x.TextKey, "lore.cat." + x.Category }));
        keys.AddRange(def.Memories.Select(x => x.TextKey));
        keys.AddRange(def.FlavourLines.Select(x => x.TextKey));
        foreach (var node in def.CoreArguments)
        {
            keys.Add(node.PromptKey);
            keys.AddRange(node.Choices.SelectMany(x => new[] { x.TextKey, x.ResponseKey }));
        }

        foreach (var key in keys)
        {
            Assert.False(en.Get(key).StartsWith("["), $"EN missing {key}");
            Assert.False(de.Get(key).StartsWith("["), $"DE missing {key}");
        }
    }

    [Fact]
    public void DefaultStory_resolves_to_voidcraft_awakening()
    {
        Assert.Equal("voidcraft_awakening", Load().DefaultStory.Id);
    }
}
