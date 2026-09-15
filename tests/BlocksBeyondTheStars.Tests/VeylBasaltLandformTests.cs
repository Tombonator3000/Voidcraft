// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Pure proposal invariants: no world generation, content loading, terrain writes or physics.
/// Actual rock support, reservations and persisted deltas are checked by the server integration suite.</summary>
public sealed class VeylBasaltLandformTests
{
    private const int SiteWidth = 25;
    private const int SiteLength = 57;
    private static readonly long[] Seeds =
    {
        0, 1, -1, 42, -42, 4242, -4242, 424242, 8675309, 1234567890123, long.MinValue, long.MaxValue,
    };

    [Fact]
    public void LegacyVersionZero_AlwaysProposesNothing_WithoutNeedingNewLayoutDimensions()
    {
        foreach (long seed in Seeds)
        {
            Assert.Empty(VeylBasaltLandformGenerator.Propose(seed, SiteWidth, SiteLength, version: 0));
            Assert.Empty(VeylBasaltLandformGenerator.Propose(seed, 0, 0, version: 0));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(VeylBasaltLandformGenerator.LatestVersion + 1)]
    public void UnknownVersion_IsRejectedRatherThanReinterpretedAsCurrentGeometry(int version)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            VeylBasaltLandformGenerator.Propose(4242, SiteWidth, SiteLength, version));
        Assert.Equal("version", error.ParamName);
    }

    [Theory]
    [InlineData(0, 57, "width")]
    [InlineData(-1, 57, "width")]
    [InlineData(25, 0, "length")]
    [InlineData(25, -1, "length")]
    public void CurrentProposal_RejectsInvalidDimensions(int width, int length, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            VeylBasaltLandformGenerator.Propose(4242, width, length));
        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void SeededProposals_AreDeterministicIndependentOfCallOrder_AndSeedsProduceVariation()
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (long seed in Seeds)
        {
            var first = VeylBasaltLandformGenerator.Propose(seed, SiteWidth, SiteLength);
            string before = ProposalSignature(first);
            _ = VeylBasaltLandformGenerator.Propose(seed ^ 0x13579BDFL, 37, 81);
            var repeated = VeylBasaltLandformGenerator.Propose(seed, SiteWidth, SiteLength);
            Assert.Equal(before, ProposalSignature(first));
            Assert.Equal(before, ProposalSignature(repeated));
            signatures.Add(before);
        }
        Assert.True(signatures.Count > 1, "Changing the seed must vary the landforms, not merely return one fixed proposal.");
    }

    [Theory]
    [InlineData(17, 17)]
    [InlineData(25, 57)]
    [InlineData(37, 81)]
    public void EachPrism_HasACompactConnectedClippedFootprint_WithoutHolesOrIsolatedCells(int width, int length)
    {
        foreach (long seed in Seeds)
        {
            var prisms = VeylBasaltLandformGenerator.Propose(seed, width, length);
            Assert.InRange(prisms.Count, 3, VeylBasaltLandformGenerator.PrismLimit);
            foreach (var prism in prisms)
            {
                Assert.InRange(prism.Cluster, 0, 2);
                Assert.InRange(prism.Height, 8, 16); // Authored geological scale, not a regenerated height formula.
                Assert.NotEmpty(prism.Footprint);
                var cells = prism.Footprint.ToHashSet();
                Assert.Equal(prism.Footprint.Count, cells.Count);
                Assert.All(cells, p =>
                {
                    Assert.InRange(p.X, -24, width + 24);
                    Assert.InRange(p.Z, -16, length + 24);
                });
                int across = cells.Max(p => p.X) - cells.Min(p => p.X) + 1;
                int along = cells.Max(p => p.Z) - cells.Min(p => p.Z) + 1;
                Assert.InRange(across, 5, 8);
                Assert.InRange(along, 5, 8);
                Assert.True(cells.Count < across * along, "The prism must retain clipped corners instead of becoming a rectangular tower.");
                Assert.True(cells.Count * 2 >= across * along, "A thin ring or sparse outline is not a solid prism footprint.");
                AssertConnected(cells);
                // A filled interval in every row and column rejects holes and detached slivers while
                // allowing the outline and dimensions to evolve without copying the generator's mask.
                foreach (var row in cells.GroupBy(p => p.Z))
                    Assert.Equal(row.Max(p => p.X) - row.Min(p => p.X) + 1, row.Count());
                foreach (var column in cells.GroupBy(p => p.X))
                    Assert.Equal(column.Max(p => p.Z) - column.Min(p => p.Z) + 1, column.Count());
            }
        }
    }

    [Fact]
    public void BudgetLimitedPrefix_AlreadyFramesBothSidesAndRear_WithUnequalPrincipalSilhouettes()
    {
        foreach (long seed in Seeds)
        {
            var prisms = VeylBasaltLandformGenerator.Propose(seed, SiteWidth, SiteLength);
            var principals = prisms.Take(3).ToArray();
            Assert.Equal(new[] { 0, 1, 2 }, principals.Select(p => p.Cluster));
            Assert.All(principals[0].Footprint, p => Assert.True(p.X < -3));
            Assert.All(principals[1].Footprint, p => Assert.True(p.X >= SiteWidth + 3));
            Assert.All(principals[2].Footprint, p => Assert.True(p.Z >= SiteLength + 3));
            var silhouettes = principals.Select(SilhouetteSignature).Distinct(StringComparer.Ordinal).ToArray();
            Assert.Equal(3, silhouettes.Length); // Translation alone must not make identical pillars count as different forms.
        }
    }

    [Fact]
    public void EveryProposal_LeavesTheFiveWideNorthApproachAndRuneReadingZoneClear()
    {
        int centre = SiteWidth / 2;
        var rune = VeylSurveyGenerator.VaultSurfaceContact;
        foreach (long seed in Seeds)
        {
            var cells = VeylBasaltLandformGenerator.Propose(seed, SiteWidth, SiteLength)
                .SelectMany(p => p.Footprint).ToHashSet();
            // Full route from the server's maximum northern approach to the open stair mouth.
            for (int x = centre - 2; x <= centre + 2; x++)
                for (int z = -64; z <= 4; z++)
                    Assert.DoesNotContain((x, z), cells);
            // A player must be able to approach and read the existing north-facing surface contact.
            for (int x = rune.X - 3; x <= rune.X + 3; x++)
                for (int z = rune.Z - 4; z <= rune.Z + 2; z++)
                    Assert.DoesNotContain((x, z), cells);
            Assert.DoesNotContain(cells, p => p.X >= -3 && p.X < SiteWidth + 3
                && p.Z >= -3 && p.Z < SiteLength + 3);
        }
    }

    [Fact]
    public void AggregateIntegerColumnsAndExposedCellWork_StayWithinFiniteProposalBudgets()
    {
        foreach (long seed in Seeds)
        {
            var prisms = VeylBasaltLandformGenerator.Propose(seed, SiteWidth, SiteLength);
            long columns = 0, projectedCells = 0;
            foreach (var prism in prisms)
            {
                // Count every proposed column at every integer level above its future base. Overlap
                // between neighboring prisms is counted twice, making this a conservative work bound.
                columns += prism.Footprint.Count;
                projectedCells += (long)prism.Footprint.Count * prism.Height;
            }
            Assert.InRange(columns, 1L, 512L);
            Assert.InRange(projectedCells, 1L, VeylBasaltLandformGenerator.AddedCellLimit);
            // Ground/seabed fill is deliberately not invented here. The server must reject/trim a
            // proposal when actual support plus these exposed columns exceed AddedCellLimit.
        }
    }

    private static string ProposalSignature(IReadOnlyList<VeylBasaltLandformGenerator.Prism> prisms)
        => string.Join("|", prisms.Select(p => $"{p.Cluster}:{p.Height}:"
            + string.Join(";", p.Footprint.OrderBy(c => c.X).ThenBy(c => c.Z).Select(c => $"{c.X},{c.Z}"))));

    private static string SilhouetteSignature(VeylBasaltLandformGenerator.Prism prism)
    {
        int minX = prism.Footprint.Min(p => p.X), minZ = prism.Footprint.Min(p => p.Z);
        return prism.Height + ":" + string.Join(";", prism.Footprint
            .Select(p => (X: p.X - minX, Z: p.Z - minZ)).OrderBy(p => p.X).ThenBy(p => p.Z)
            .Select(p => $"{p.X},{p.Z}"));
    }

    private static void AssertConnected(HashSet<(int X, int Z)> cells)
    {
        var visited = new HashSet<(int X, int Z)>();
        var open = new Queue<(int X, int Z)>();
        var first = cells.First();
        visited.Add(first); open.Enqueue(first);
        while (open.Count > 0)
        {
            var p = open.Dequeue();
            foreach (var adjacent in new[] { (p.X - 1, p.Z), (p.X + 1, p.Z), (p.X, p.Z - 1), (p.X, p.Z + 1) })
                if (cells.Contains(adjacent) && visited.Add(adjacent)) open.Enqueue(adjacent);
        }
        Assert.True(visited.SetEquals(cells), "Every footprint cell must join the same solid prism by a full voxel edge.");
    }
}
