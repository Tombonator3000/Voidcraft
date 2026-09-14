// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>The first Veyl expedition joins physical excavation and construction to the existing scan,
/// monument, story and gadget systems. Only player milestones and changed voxels are persisted.</summary>
public sealed partial class GameServer
{
    private const string SurveyKind = "veyl_survey";
    private const string SurveyRewarded = "survey:veyl:rewarded";
    private const string SurveyReturned = "survey:veyl:response";
    private const string SurveySiteResponse = "veyl_survey:response";
    private static string SurveySpecimenKey(string shipId) => "survey:veyl:specimen:" + shipId;
    private string SurveyKey(string step) => $"survey:veyl:{_world.LocationId}:{step}";
    private bool SurveyEnabled => !string.IsNullOrEmpty(_story?.SurveyRewardItemKey);

    private static Vector3i? SurveyMarker(PlacedSettlement placement, string type)
    {
        foreach (var marker in placement.Structure.Markers)
        {
            if (marker.Type == type)
            {
                return new Vector3i(placement.Origin.X + marker.LocalPos.X,
                    placement.GroundY + marker.LocalPos.Y, placement.Origin.Z + marker.LocalPos.Z);
            }
        }
        return null;
    }

    private void StampVeylSurvey()
    {
        if (!SurveyEnabled || _meta.Description.Settlements.StructureFactor() <= 0) return;
        var record = FindPlacementRecord(SurveyKind, 0);
        // One introductory expedition per save. Existing worlds are never reseated or regenerated.
        if (record is null && _meta.Placements.Any(p => p.Kind == SurveyKind)) return;
        int geometryVersion = record?.GeometryVersion ?? VeylSurveyGenerator.LatestVersion;
        var structure = VeylSurveyGenerator.Generate(_content, "stone", geometryVersion);
        var rng = RngFor(_meta.Seed, SurveyKind);
        Vector3i origin;
        int ground;
        VeylTerrainPlan? terrainPlan = null;
        string seat = "buried";
        if (record is not null)
        {
            if (!record.Placed) return;
            origin = new Vector3i(record.X, record.GroundY, record.Z);
            ground = record.GroundY;
        }
        else
        {
            var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
            foreach (var pad in _landingPads)
                reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
            foreach (var m in _monuments)
                reserved.Add(((m.Min.X + m.Max.X) / 2, (m.Min.Z + m.Max.Z) / 2, (m.Max.X - m.Min.X) / 2, (m.Max.Z - m.Min.Z) / 2));
            foreach (var s in _settlements)
                reserved.Add(((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2, (s.Max.X - s.Min.X) / 2, (s.Max.Z - s.Min.Z) / 2));
            foreach (var c in _banditCamps)
                reserved.Add(((c.Min.X + c.Max.X) / 2, (c.Min.Z + c.Max.Z) / 2, (c.Max.X - c.Min.X) / 2, (c.Max.Z - c.Min.Z) / 2));
            AddVeylContentReservations(reserved);
            int px = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
            int pz = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
            reserved.Add((px - 56, pz + 56, 18, 18)); // starter wreck reserve
            origin = default;
            ground = 0;
            bool found = false;
            // A short walk from the landing pad; reject water, cliffs and existing player buildings.
            for (int attempt = 0; attempt < 160; attempt++)
            {
                int distance = 72 + attempt / 32 * 32;
                double angle = (attempt % 32) * Math.PI / 16;
                int cx = px + (int)Math.Round(Math.Cos(angle) * distance);
                int cz = pz + (int)Math.Round(Math.Sin(angle) * distance);
                int ox = cx - structure.Width / 2, oz = cz - structure.Length / 2;
                if (OverlapsFootprint(cx, cz, (structure.Width + 1) / 2, (structure.Length + 1) / 2, reserved, 6)
                    || FootprintWet(_world.Planet, ox, oz, structure.Width, structure.Length)
                    || FootprintSpread(_world.Planet, ox, oz, structure.Width, structure.Length) > 6) continue;
                ground = _generator.SurfaceHeight(_world.Planet, cx, cz) - VeylSurveyGenerator.BurialDepthFor(geometryVersion);
                if (FootprintHasPlayerEdits(ox, oz, ground, structure.Width, structure.Height, structure.Length)) continue;
                origin = new Vector3i(ox, ground, oz);
                found = true;
                break;
            }
            if (!found && _worlds.Active.VirginAtLoad)
                found = TryPlaceVeylTerrace(structure, geometryVersion, reserved,
                    out origin, out ground, out seat, out terrainPlan);
            if (!found)
            {
                ReportStamp(SurveyKind, 1, 0);
                return; // preserve the world; another newly visited body may provide a safe site
            }
        }

        var placement = new PlacedSettlement
        {
            Structure = structure,
            Origin = origin,
            GroundY = ground,
            Tier = "monument",
            Ruined = true,
            OnIsland = false,
            Name = "veyl_anchor",
            Rng = rng,
        };
        if (record is null)
        {
            var stampTimer = System.Diagnostics.Stopwatch.StartNew();
            int cellOperations = 0;
            _repo.RunInTransaction(() =>
            {
                if (terrainPlan is not null) cellOperations = StampVeylTerrace(terrainPlan);
                cellOperations += StampMonumentBlocks(placement, geometryVersion);
                RecordPlacement(SurveyKind, 0, origin, ground, false, seat, "veyl_anchor", geometryVersion);
                if (terrainPlan is not null)
                {
                    var pinned = FindPlacementRecord(SurveyKind, 0)!;
                    pinned.Reservation = new StructureReservationBounds
                    {
                        MinX = terrainPlan.Min.X,
                        MinY = terrainPlan.Min.Y,
                        MinZ = terrainPlan.Min.Z,
                        MaxX = terrainPlan.Max.X,
                        MaxY = terrainPlan.Max.Y,
                        MaxZ = terrainPlan.Max.Z,
                    };
                }
                SavePlacementRecords();
            });
            _log.Info($"Veyl geometry v{geometryVersion}: {cellOperations} cell operations in {stampTimer.ElapsedMilliseconds} ms.");
        }
        RegisterMonument(placement, "veyl_anchor");
        ReportStamp(SurveyKind, 1, 1);
    }

    private bool SurveyContactReachable(PlayerSession session, Vector3i contact)
    {
        if (session.CurrentLocationId != _world.LocationId || session.State.AboardShip
            || !session.State.Inventory.Slots.Any(s => s is not null
                && _content.GetItem(s.Item)?.Tool?.Kind == ToolKind.Scanner)
            || WrapDistSq(session.State.Position, new Vector3f(contact.X + 0.5f, contact.Y + 0.5f, contact.Z + 0.5f)) > 36f
            || _content.BlockById(_world.GetBlock(contact))?.Key != "rune_stone") return false;
        // The authored inscription faces the entry stair. Two air cells prove a standing reading position
        // has actually been excavated; proximity through solid terrain alone cannot unlock the survey.
        return _world.GetBlock(contact + new Vector3i(0, 0, -1)).IsAir
            && _world.GetBlock(contact + new Vector3i(0, 1, -1)).IsAir;
    }

    private bool VeylSurveyCanRead(PlayerSession session, MonumentInstance monument)
        => (monument.SurfaceContact is { } surface && SurveyContactReachable(session, surface))
            || (monument.BuriedContact is { } core && SurveyContactReachable(session, core));

    private string ScanVeylSurvey(PlayerSession session, MonumentInstance monument)
    {
        if (!SurveyEnabled) return "ui.scan.monument.veyl_anchor";
        if (monument.BuriedContact is { } core && SurveyContactReachable(session, core))
        {
            if (session.State.Milestones.Add(SurveyKey("contact")))
            {
                session.State.Milestones.Add(SurveyKey("located"));
                PersistSurveyStep(session, "survey.veyl.shape");
            }
            // A later explorer can read a repair already made by another player, without destroying it.
            TryAnswerVeylSurvey(session, monument);
        }
        else if (monument.SurfaceContact is { } surface && SurveyContactReachable(session, surface)
                 && session.State.Milestones.Add(SurveyKey("located")))
        {
            PersistSurveyStep(session, "survey.veyl.excavate");
        }
        return "survey.veyl." + SurveyStage(session);
    }

    private string SurveyStage(PlayerSession session)
        => session.State.Milestones.Contains(SurveyRewarded) ? "complete"
            : session.State.Milestones.Contains(SurveyReturned) ? "return"
            : session.State.Milestones.Contains(SurveyKey("contact")) ? "shape"
            : session.State.Milestones.Contains(SurveyKey("located")) ? "excavate" : "signal";

    private void PersistSurveyStep(PlayerSession session, string textKey)
    {
        _repo.SavePlayer(session.State);
        SendVegaLine(session, textKey, 2);
        SendPlanetPois(session);
    }

    private void VeylSurveyOnPlace(PlayerSession session, Vector3i position)
    {
        if (!SurveyEnabled || session.State.Milestones.Contains(SurveyReturned)
            || !session.State.Milestones.Contains(SurveyKey("contact"))) return;
        var monument = _monuments.FirstOrDefault(m => m.RepairSocket is { } socket
            && WorldConstants.CanonicalBlock(socket, _world.Circumference) == position);
        if (monument is not null) TryAnswerVeylSurvey(session, monument);
    }

    private void TryAnswerVeylSurvey(PlayerSession session, MonumentInstance monument)
    {
        if (session.State.Milestones.Contains(SurveyReturned)
            || !session.State.Milestones.Contains(SurveyKey("contact"))
            || monument.RepairSocket is not { } position
            || monument.BuriedContact is not { } core || !SurveyContactReachable(session, core)) return;
        var block = _content.BlockById(_world.GetBlock(position));
        var support = position + new Vector3i(0, -1, 0);
        if (block is not { Shapeable: true, Solid: true }
            || ShapeCode.ShapeOf(_world.GetShape(position)) == 0
            || _content.BlockById(_world.GetBlock(support)) is not { Solid: true }
            || ShapeCode.ShapeOf(_world.GetShape(support)) != 0) return;
        session.State.Milestones.Add(SurveyReturned);
        _repo.RunInTransaction(() =>
        {
            foreach (var contact in new[] { monument.SurfaceContact, monument.BuriedContact })
            {
                if (contact is not { } p || _content.BlockById(_world.GetBlock(p))?.Key != "rune_stone") continue;
                int shape = _world.GetShape(p);
                var id = _world.GetBlock(p);
                _world.SetBlock(p, id, 0x91D5DF, 0x66ECFF, shape);
                BroadcastToWorld(new BlockChanged
                {
                    X = p.X,
                    Y = p.Y,
                    Z = p.Z,
                    Block = id.Value,
                    Tint = 0x91D5DF,
                    Glow = 0x66ECFF,
                    Shape = shape
                });
            }
            _repo.SavePlayer(session.State);
            // The physical response is shared; personal acknowledgements never farm the shared story arc.
            if (!FeatureStamped(SurveySiteResponse))
            {
                MarkFeatureStamped(SurveySiteResponse);
                RecordStoryMilestone();
            }
        });
        RevealShapeAnomalyMemory(session); // existing pack-aware insight, still respects its spoiler gate
        SendVegaLine(session, "survey.veyl.return", 2);
        SendPlanetPois(session);
    }

    private bool SurveyAtOwnShip(PlayerSession session)
    {
        if (!session.State.AboardShip || session.CurrentLocationId != _world.LocationId
            || InSpace(session.State.PlayerId) || InStation(session.State.PlayerId)
            || string.IsNullOrEmpty(session.ActiveShipId) || !session.Ships.ContainsKey(session.ActiveShipId)) return false;
        var home = _worlds.Active.LandedFor(session.State.PlayerId);
        if (!home.Placed) return false;
        var p = session.State.Position;
        double dx = WorldConstants.WrapDeltaX(p.X - home.Origin.X, _world.Circumference);
        double dz = WorldConstants.WrapDeltaZ(p.Z - home.Origin.Z, _world.Circumference);
        return dx >= 0 && dx <= home.Structure.Width
            && p.Y >= home.Origin.Y && p.Y <= home.Origin.Y + home.Structure.Height + 1
            && dz >= 0 && dz <= home.Structure.Length;
    }

    private void TickVeylSurveyHomecoming(PlayerSession session)
    {
        if (!SurveyEnabled || !SurveyAtOwnShip(session)
            || !session.State.Milestones.Contains(SurveyReturned)
            || session.State.Milestones.Contains(SurveyRewarded)
            || _content.GetItem(_story!.SurveyRewardItemKey) is not { } reward) return;
        // The periodic caller visits multiple players. Inventory snapshots include the current ship cargo.
        SetCurrent(session);
        // Add one indivisible item. A full pack retains the pending milestone and retries on the next tick.
        if (session.State.Inventory.Add(reward.Key, 1, reward.MaxStack) != 0)
        {
            if (session.State.Milestones.Add("survey:veyl:make_room"))
                PersistSurveyStep(session, "survey.veyl.make_room");
            return;
        }
        session.State.Milestones.Add(SurveyRewarded);
        session.State.Milestones.Add(SurveySpecimenKey(session.ActiveShipId));
        if (_content.Blueprints.ContainsKey(reward.Key)) session.State.UnlockedBlueprints.Add(reward.Key);
        PersistSurveyStep(session, "survey.veyl.complete");
        SendInventory(session);
        SendPlayerState(session);
        BroadcastToWorld(LandedShipMessage(session.State.PlayerId, _worlds.Active.LandedFor(session.State.PlayerId), removed: false));
    }

    private void AddVeylSurveyPois(PlayerSession session, List<NetPoi> pois)
    {
        if (!SurveyEnabled) return;
        string stage = SurveyStage(session);
        if (stage == "return")
        {
            var home = _worlds.Active.LandedFor(session.State.PlayerId);
            // No ground marker exists in a station or while the owned ship is in flight.
            if (!home.Placed || InSpace(session.State.PlayerId) || InStation(session.State.PlayerId)) return;
            var anchor = AnchorOf(home);
            pois.Add(new NetPoi
            {
                Type = "veyl_return",
                Name = Localize(session.Locale, "poi.veyl_return"),
                X = anchor.X + 0.5f,
                Z = anchor.Z + 0.5f
            });
            return;
        }
        foreach (var monument in _monuments)
        {
            if (monument.SurfaceContact is not { } contact) continue;
            var target = stage == "shape" ? monument.RepairSocket ?? contact
                : stage == "excavate" ? monument.BuriedContact ?? contact : contact;
            pois.Add(new NetPoi
            {
                Type = "veyl_" + stage,
                Name = Localize(session.Locale, "poi.veyl_" + stage),
                X = target.X + 0.5f,
                Z = target.Z + 0.5f
            });
        }
    }

    /// <summary>Inspection seam for a real stamped expedition; no synthetic geometry or progress.</summary>
    public (Vector3i Surface, Vector3i Contact, Vector3i Socket)? VeylSurveyForTest()
        => _monuments.FirstOrDefault(m => m.Archetype == "veyl_anchor") is
        { SurfaceContact: { } surface, BuriedContact: { } core, RepairSocket: { } socket }
            ? (surface, core, socket) : null;

    public void VeylSurveyHomecomingForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session) TickVeylSurveyHomecoming(session);
    }
}
