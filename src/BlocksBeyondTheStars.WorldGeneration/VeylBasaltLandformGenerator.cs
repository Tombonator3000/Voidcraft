// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A site-local geological silhouette proposal, independent of planet generation. The server
/// validates actual ground, water, support, access, ownership and reservations before stamping anything.
/// This helper does not call or change WorldGenerator's global basalt hotspots or surface heights.</summary>
public static class VeylBasaltLandformGenerator
{
    public const int LatestVersion = 1;
    public const int AddedCellLimit = 20000;
    public const int PrismLimit = 9;
    public const int SupportDepthLimit = 96;

    public sealed class Prism
    {
        public int Cluster { get; }
        public int Height { get; }
        public IReadOnlyList<(int X, int Z)> Footprint { get; }

        internal Prism(int cluster, int height, IReadOnlyList<(int X, int Z)> footprint)
        {
            Cluster = cluster;
            Height = height;
            Footprint = footprint;
        }
    }

    /// <summary>Three uneven clusters flank the site and close its rear. Their local integer masks are
    /// deterministic, so translating a site through either world seam cannot change its silhouette.</summary>
    public static IReadOnlyList<Prism> Propose(long seed, int width, int length, int version = LatestVersion)
    {
        if (version == 0) return Array.Empty<Prism>();
        if (version != LatestVersion) throw new ArgumentOutOfRangeException(nameof(version));
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        var result = new List<Prism>(PrismLimit);
        // Emit one principal column per cluster first. If safety/budget rejects extras, the remaining
        // formations still frame more than one side instead of spending the whole budget on one wall.
        for (int neighbour = 0; neighbour < 3; neighbour++)
            for (int cluster = 0; cluster < 3; cluster++)
            {
                ulong h = Noise.Hash(seed ^ 0x5645594C424153L, cluster, neighbour, 1);
                int jitterX = (int)(h & 3) - 1;
                int jitterZ = (int)((h >> 2) & 3) - 1;
                int cx = cluster switch { 0 => -13, 1 => width + 12, _ => width / 2 };
                int cz = cluster switch { 0 => length / 3, 1 => length / 2 + 5, _ => length + 12 };
                cx += jitterX + (neighbour == 1 ? -5 : neighbour == 2 ? 5 : 0);
                cz += jitterZ + (neighbour == 0 ? 0 : 3);
                double radius = 3.0 + ((h >> 8) & 3) / 6.0; // six-cell silhouette with visibly clipped hexagonal corners
                var cells = new List<(int X, int Z)>();
                int bound = (int)Math.Ceiling(radius);
                for (int x = cx - bound; x <= cx + bound; x++)
                    for (int z = cz - bound; z <= cz + bound; z++)
                    {
                        double dx = Math.Abs(x + 0.5 - cx), dz = Math.Abs(z + 0.5 - cz);
                        // Voxelised pointy hexagon, retaining a recognisable prism at normal voxel scale.
                        if (dx <= radius * 0.8660254037844386 && dz + dx * 0.5773502691896258 <= radius)
                            cells.Add((x, z));
                    }
                // The three principal silhouettes have distinct height bands even when a seed's
                // radius hashes coincide. Subsidiary columns retain wider irregular variation.
                int height = neighbour == 0 ? cluster switch
                {
                    0 => 12 + (int)((h >> 16) % 3),
                    1 => 8 + (int)((h >> 16) % 3),
                    _ => 15 + (int)((h >> 16) % 2),
                } : 8 + (int)(((h >> 16) + (ulong)(neighbour * 3)) % 9);
                result.Add(new Prism(cluster, height, cells));
            }
        return result;
    }
}
