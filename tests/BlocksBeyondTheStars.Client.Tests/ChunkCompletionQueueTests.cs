// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;

namespace BlocksBeyondTheStars.Client.Tests;

public sealed class ChunkCompletionQueueTests
{
    [Fact]
    public void NearbyCollisionCompletionPrecedesDistantOlderWork()
    {
        var queue = new ChunkCompletionQueue<(string Id, float Distance)>();
        queue.Enqueue(("far", 100f));
        queue.Enqueue(("footing", 1f));
        queue.Enqueue(("middle", 25f));

        Assert.True(queue.TryDequeue(x => x.Distance, out var first));
        Assert.Equal("footing", first.Id);
        Assert.True(queue.TryDequeue(x => x.Distance, out var second));
        Assert.Equal("middle", second.Id);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void ContinuousNearbyEditsCannotStarveOldCompletions()
    {
        var queue = new ChunkCompletionQueue<(string Id, float Distance)>();
        queue.Enqueue(("oldest", 100f));
        queue.Enqueue(("next-oldest", 200f));
        var served = new List<string>();
        for (int i = 0; i < 16; i++)
        {
            queue.Enqueue(($"near-{i}", 0f));
            Assert.True(queue.TryDequeue(x => x.Distance, out var item));
            served.Add(item.Id);
        }

        Assert.Equal("oldest", served[7]);
        Assert.Equal("next-oldest", served[15]);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void EmptyQueueDoesNotInvokeDistanceSelectorOrLoseLaterWork()
    {
        var queue = new ChunkCompletionQueue<int>();
        Assert.False(queue.TryDequeue(_ => throw new InvalidOperationException(), out _));
        queue.Enqueue(42);
        Assert.True(queue.TryDequeue(_ => 0f, out var item));
        Assert.Equal(42, item);
        Assert.Equal(0, queue.Count);
    }
}
