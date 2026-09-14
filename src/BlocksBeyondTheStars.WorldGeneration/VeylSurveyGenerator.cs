// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A buried survey chamber with a visible monolith and an open descending stair. The contact
/// and its repair socket remain under mineable rubble; geometry is shared by the stamper and validation.
/// This authored monument reuses voxel shapes and the persisted monument pipeline.</summary>
public static class VeylSurveyGenerator
{
    public const int LatestVersion = 3;
    public const int BurialDepth = 6; // Version 0/1: preserve saved contact coordinates and cells.
    public const int VaultBurialDepth = 26;
    public static readonly Vector3i VaultSurfaceContact = new(8, 27, 3);
    public static readonly Vector3i VaultBuriedContact = new(12, 5, 47);
    public static readonly Vector3i VaultRepairSocket = new(13, 5, 47);

    public static int BurialDepthFor(int geometryVersion) => geometryVersion switch
    {
        0 or 1 => BurialDepth,
        2 or LatestVersion => VaultBurialDepth,
        _ => throw new System.ArgumentOutOfRangeException(nameof(geometryVersion)),
    };

    /// <summary>Only the open approach needs sky clearance. Clearing every vault column above the roof
    /// would strip its natural overburden and reveal a rectangular box on the surface.</summary>
    public static int MinimumCarveHeight(int geometryVersion, int x, int z)
        => geometryVersion switch
        {
            0 or 1 => BurialDepth + 4,
            2 or LatestVersion => (x >= 10 && x <= 14 && z <= 27) || (x >= 6 && x <= 16 && z <= 4)
                ? VaultBurialDepth + 4 : z == 27 && x >= 3 && x <= 14 ? 8 : 0,
            _ => throw new System.ArgumentOutOfRangeException(nameof(geometryVersion)),
        };
    public static readonly Vector3i SurfaceContact = new(11, 8, 10);
    public static readonly Vector3i BuriedContact = new(8, 1, 9);
    public static readonly Vector3i RepairSocket = new(9, 1, 9);

    public static SettlementStructure Generate(GameContent content, string surfaceBlock, int geometryVersion = LatestVersion)
        => geometryVersion switch
        {
            0 or 1 => GenerateLegacy(content, surfaceBlock),
            2 => GenerateVault(content, surfaceBlock, interruptedBridge: false),
            LatestVersion => GenerateVault(content, surfaceBlock, interruptedBridge: true),
            _ => throw new System.ArgumentOutOfRangeException(nameof(geometryVersion)),
        };

    private static SettlementStructure GenerateLegacy(GameContent content, string surfaceBlock)
    {
        const int Size = 17;
        const int Height = 17;
        ushort stone = content.GetBlock("stone")!.NumericId.Value;
        ushort shell = content.GetBlock("basalt")?.NumericId.Value ?? stone;
        ushort rune = content.GetBlock("rune_stone")?.NumericId.Value ?? stone;
        ushort rubble = content.GetBlock(surfaceBlock)?.NumericId.Value ?? stone;
        var blocks = new ushort[Size * Height * Size];
        var modifiers = new Dictionary<int, (int Tint, int Glow)>();
        var shapes = new Dictionary<int, int>();
        int Index(int x, int y, int z) => (x * Height + y) * Size + z;
        void Set(int x, int y, int z, ushort block) => blocks[Index(x, y, z)] = block;

        for (int x = 3; x <= 13; x++)
            for (int z = 3; z <= 13; z++)
            {
                Set(x, 0, z, shell);
                for (int y = 1; y <= BurialDepth; y++)
                {
                    bool wall = x == 3 || x == 13 || z == 3 || z == 13;
                    Set(x, y, z, wall ? shell : rubble);
                }
            }

        // Three-wide stair, two blocks of headroom plus sky clearance. Its open mouth is visible beside
        // the monolith; excavation begins at the final step, not inside an inaccessible sealed cube.
        for (int z = 0; z <= 6; z++)
            for (int x = 7; x <= 9; x++)
            {
                int step = System.Math.Max(0, BurialDepth - z);
                for (int y = 0; y <= step; y++) Set(x, y, z, shell);
                for (int y = step + 1; y <= 10; y++) Set(x, y, z, 0);
            }

        for (int y = BurialDepth + 1; y <= 15; y++)
            for (int x = 10; x <= 12; x++)
                for (int z = 10; z <= 12; z++)
                    Set(x, y, z, shell);
        Set(11, 16, 11, shell);
        shapes[Index(11, 16, 11)] = ShapeCode.Pack(BlockShape.Pyramid, 0);
        foreach (var contact in new[] { SurfaceContact, BuriedContact })
        {
            Set(contact.X, contact.Y, contact.Z, rune);
            modifiers[Index(contact.X, contact.Y, contact.Z)] = (0x577B87, 0x237A8C);
        }
        Set(RepairSocket.X, 0, RepairSocket.Z, shell);
        modifiers[Index(RepairSocket.X, 0, RepairSocket.Z)] = (0xA8874B, 0x604522);
        var markers = new List<SettlementMarker>
        {
            new("survey_surface", SurfaceContact),
            new("survey_contact", BuriedContact),
            new("survey_socket", RepairSocket),
        };
        return new SettlementStructure(Size, Height, Size, "monument:veyl_anchor", ruined: true,
            inhabitant: string.Empty, blocks, markers, buildingCount: 1, modifiers, shapes);
    }


    private static SettlementStructure GenerateVault(GameContent content, string surfaceBlock, bool interruptedBridge)
    {
        const int Width = 25, Height = 45, Length = 57;
        ushort stone = content.GetBlock("stone")!.NumericId.Value;
        ushort shell = content.GetBlock("basalt")?.NumericId.Value ?? stone;
        ushort dark = content.GetBlock("obsidian")?.NumericId.Value ?? shell;
        ushort rune = content.GetBlock("rune_stone")?.NumericId.Value ?? stone;
        ushort warm = content.GetBlock("strip_light_warm")?.NumericId.Value ?? rune;
        ushort rubble = content.GetBlock(surfaceBlock)?.NumericId.Value ?? stone;
        var blocks = new ushort[Width * Height * Length];
        var modifiers = new Dictionary<int, (int Tint, int Glow)>();
        var shapes = new Dictionary<int, int>();
        int Index(int x, int y, int z) => (x * Height + y) * Length + z;
        void Set(int x, int y, int z, ushort block)
        {
            int i = Index(x, y, z);
            blocks[i] = block;
            modifiers.Remove(i);
            shapes.Remove(i);
        }
        void Box(int x0, int y0, int z0, int x1, int y1, int z1, ushort block)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++) Set(x, y, z, block);
        }
        void Signal(int x, int y, int z)
        {
            Set(x, y, z, rune);
            modifiers[Index(x, y, z)] = (0x577B87, 0x237A8C);
        }

        // A narrow surface landing and a real 22 m descent keep the tall chamber underground. Each
        // meter drops through two half-meter risers, compatible with the ordinary controller's step.
        Box(6, 0, 0, 16, 26, 4, shell);
        for (int z = 0; z <= 21; z++)
        {
            int step = VaultBurialDepth - z;
            Box(10, 0, z, 14, step, z, shell);
            Box(11, step + 1, z, 13, 30, z, 0);
            for (int x = 11; x <= 13; x++)
                shapes[Index(x, step, z)] = ShapeCode.Pack(BlockShape.Stairs, 2);
            // Low side walls describe the trench without narrowing the three-meter walking volume.
            Box(10, step + 1, z, 10, step + 1, z, shell);
            Box(14, step + 1, z, 14, step + 1, z, shell);
        }
        Box(10, 0, 22, 14, 4, 27, shell);
        Box(8, 0, 3, 8, 27, 3, shell);
        Signal(VaultSurfaceContact.X, VaultSurfaceContact.Y, VaultSurfaceContact.Z);

        // A supported stepped tower is the surface landmark; the much larger vault roof sits at the
        // sampled natural surface, rather than twenty meters above it.
        Box(18, 0, 9, 20, 40, 11, shell);
        Box(19, 41, 10, 19, 43, 10, shell);
        Set(19, 44, 10, shell);
        shapes[Index(19, 44, 10)] = ShapeCode.Pack(BlockShape.Pyramid, 0);
        for (int y = 29; y <= 39; y++) Signal(19, y, 9);

        // A real hollow volume: the foundation makes every interior column participate in stamping,
        // so natural terrain is removed from the pit and the negative space around the Anchor.
        Box(0, 0, 28, 24, 0, 56, shell);
        Box(0, 26, 28, 24, 26, 56, shell);
        Box(0, 1, 28, 0, 25, 56, shell);
        Box(24, 1, 28, 24, 25, 56, shell);
        Box(0, 1, 56, 24, 25, 56, shell);
        // Entry shoulders and lintel frame the first reveal but leave the landing and escape stair open.
        Box(0, 1, 28, 2, 25, 28, shell);
        Box(17, 1, 28, 24, 25, 28, shell);
        Box(3, 18, 28, 16, 25, 28, shell);
        for (int frame = 0; frame < 3; frame++)
        {
            int x0 = 2 + frame * 2, x1 = 22 - frame * 2;
            int y0 = 1 + frame * 2, y1 = 24 - frame * 2;
            int z = 54 - frame * 2;
            Box(x0, y0, z, x0, y1, z, dark);
            Box(x1, y0, z, x1, y1, z, dark);
            Box(x0, y0, z, x1, y0, z, dark);
            Box(x0, y1, z, x1, y1, z, dark);
            for (int y = y0 + 2; y < y1; y += 4)
            {
                Signal(x0, y, z);
                Signal(x1, y, z);
            }
        }

        Box(11, 4, 28, 13, 4, 46, shell);
        for (int z = 33; z <= 42; z += 3) Set(13, 4, z, 0);
        Box(9, 4, 46, 16, 4, 49, shell);
        if (interruptedBridge)
        {
            // Repairing three ordinary cells opens a direct route. A continuous two-meter side gallery
            // also reaches the seal, so missing materials or reduced mobility never require a jump.
            Box(11, 4, 35, 13, 4, 37, 0);
            Box(7, 4, 28, 8, 4, 45, shell);
            Box(7, 4, 28, 13, 4, 29, shell);
            Box(7, 4, 44, 13, 4, 45, shell);
            for (int z = 30; z <= 42; z += 4) Box(7, 1, z, 8, 3, z, dark);
            Set(7, 4, 33, warm);
            Set(7, 4, 40, warm);
        }
        // Falling from the optional broken edge is recoverable. The pit floor leads to four stair rows
        // and a wide side landing that reconnects to the entry, without requiring a jump or mined exit.
        Box(3, 4, 27, 14, 4, 27, shell);
        Box(3, 0, 28, 4, 0, 31, shell);
        for (int z = 28; z <= 31; z++)
            for (int x = 3; x <= 4; x++)
            {
                int step = 32 - z;
                Box(x, 1, z, x, step, z, shell);
                shapes[Index(x, step, z)] = ShapeCode.Pack(BlockShape.Stairs, 2);
            }
        Set(10, 5, 25, warm);
        Set(16, 5, 48, warm);

        // The reachable inscription stays hidden behind a small excavatable curtain. The surrounding
        // chamber remains visible; there is no requirement to mine an entire solid room to discover it.
        Box(11, 5, 46, 14, 7, 46, rubble);
        Set(VaultRepairSocket.X, VaultRepairSocket.Y, VaultRepairSocket.Z, rubble);
        Signal(VaultBuriedContact.X, VaultBuriedContact.Y, VaultBuriedContact.Z);
        modifiers[Index(VaultRepairSocket.X, 4, VaultRepairSocket.Z)] = (0xA8874B, 0x604522);

        // Suspended but editable ordinary blocks: stepped massing and pointed ends have no per-block
        // objects or rigid bodies. Space beneath remains clear above the player route and seal.
        for (int y = 11; y <= 22; y++)
        {
            int radius = y <= 12 || y >= 21 ? 0 : y <= 14 || y >= 19 ? 1 : 2;
            Box(12 - radius, y, 50 - radius, 12 + radius, y, 50 + radius, dark);
            Signal(12, y, 50 - radius);
        }
        shapes[Index(12, 11, 50)] = ShapeCode.Pack(BlockShape.Pyramid, 0, 1);
        shapes[Index(12, 22, 50)] = ShapeCode.Pack(BlockShape.Pyramid, 0);
        var markers = new List<SettlementMarker>
        {
            new("survey_surface", VaultSurfaceContact),
            new("survey_contact", VaultBuriedContact),
            new("survey_socket", VaultRepairSocket),
        };
        return new SettlementStructure(Width, Height, Length, "monument:veyl_anchor", ruined: true,
            inhabitant: string.Empty, blocks, markers, buildingCount: 1, modifiers, shapes);
    }
}
