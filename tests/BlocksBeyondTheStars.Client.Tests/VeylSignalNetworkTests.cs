// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

[Trait("Suite", "ClientCore")]
public sealed class VeylSignalNetworkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WireEvent_ReachesTheDedicatedPresentationChannel_InOrderAfterPermanentDelta(bool json)
    {
        var link = new LoopbackLink();
        using var server = new LoopbackServerTransport(link);
        using var client = new NetworkClient(new LoopbackClientTransport(link));
        var received = new List<string>();
        client.BlockChanged += _ => received.Add("block");
        client.VeylSignalReceived += m => received.Add(m.EventId);
        client.StoryStateReceived += _ => received.Add("story");
        byte[] Encode(object m) => json ? NetCodec.EncodeJson(m) : NetCodec.Encode(m);
        server.Send(1, Encode(new BlockChanged { X = 1, Y = 2, Z = 3, Glow = 0x66ECFF }), DeliveryMode.ReliableOrdered);
        server.Send(1, Encode(new VeylSignalResponse
        {
            EventId = "site-response", Nodes = new[] { new VeylSignalNode { X = 1, Y = 2, Z = 3 } },
        }), DeliveryMode.ReliableOrdered);
        client.Poll();
        Assert.Equal(new[] { "block", "site-response" }, received);
    }
}
