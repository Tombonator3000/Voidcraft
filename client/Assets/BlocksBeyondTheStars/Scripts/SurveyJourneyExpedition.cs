// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    public sealed partial class SurveyJourneyProbe
    {
        private readonly List<Vector3> _breadcrumbs = new();
        private readonly HashSet<Vector3Int> _mineCells = new();
        private Dictionary<string, int> _beforeExcavation;
        private Vector3Int _stairCell, _coreCell, _socketCell, _pendingMine;
        private Vector3 _stairStart, _entryDirection;
        private bool _returning, _coreScan, _shapedPlacement, _response, _homecoming, _rewardAction, _reloadVerified;
        private bool _mineWaiting, _runeResponded;
        private int _excavated, _returnIndex;
        private double _nextMine, _stableSince;
        private string _materialItem, _acquireEvidencePath;
        private float _energyBeforeUse;
        private CraftResult _craft;
        private OreScanResult _oreScan;

        private void ObserveActions()
        {
            _network.MiningProgressReceived += m =>
            {
                Log("mining_progress", $"{m.X},{m.Y},{m.Z}={m.Fraction}");
                if (_mineWaiting && SameCell(_pendingMine, m.X, m.Y, m.Z)) _mineWaiting = false;
            };
            _network.BlockChanged += m =>
            {
                Log("block_reply", $"{m.X},{m.Y},{m.Z}: block={m.Block}, shape={m.Shape}, glow={m.Glow}");
                if (_mineWaiting && SameCell(_pendingMine, m.X, m.Y, m.Z))
                {
                    _mineWaiting = false;
                    if (m.Block == 0) _excavated++;
                }
                if (_coreScan && SameCell(_coreCell, m.X, m.Y, m.Z) && m.Glow == 0x66ECFF) _runeResponded = true;
                if (_step == Step.Response && SameCell(_socketCell, m.X, m.Y, m.Z)
                    && m.Block != 0 && ShapeCode.ShapeOf(m.Shape) != 0) _shapedPlacement = true;
            };
            _network.CraftCompleted += m => { _craft = m; Log("craft_reply", m.RecipeKey + ":" + m.Success + ":" + m.Reason); };
            _network.OreScanReceived += m => { _oreScan = m; Log("ore_scan_reply", "hits=" + m.X.Length + ", seconds=" + m.Seconds); };
        }

        private bool SameCell(Vector3Int cell, int x, int y, int z)
            => cell.y == y && Mathf.Abs(WorldConstants.WrapDeltaX(cell.x - x, _game.Circumference)) < 0.1f
                && Mathf.Abs(WorldConstants.WrapDeltaZ(cell.z - z, _game.Circumference)) < 0.1f;

        private int ItemCount(string item) => _game?.Personal?.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;
        private string FirstItem(Func<string, bool> predicate)
            => _game.Personal.Where(s => s.Count > 0 && predicate(s.Item)).Select(s => s.Item).FirstOrDefault();

        private void BeginEquip(string item, Step after)
        {
            _equipItem = item; _afterEquip = after;
            Enter(Step.Equip, 15);
        }

        private void FinishEquip()
        {
            Log("equipped", _equipItem);
            if (_afterEquip == Step.SignalWalk) BeginSignalWalk();
            else Enter(_afterEquip, _afterEquip == Step.Excavate || _afterEquip == Step.SocketClear ? 90 : 15);
        }

        private void BeginDescent()
        {
            if (!FindEntryStair(out _stairCell))
            { Finish("failed", "no_loaded_walkable_stair_near_observed_signal", 1); return; }
            _stairStart = _stairCell;
            if (!StandOn(_stairCell, out _goal))
            { Finish("failed", "observed_stair_has_no_body_clearance", 1); return; }
            Enter(Step.StairApproach, 50);
        }

        private void DriveExpedition(ref JourneyInputSource.Frame input)
        {
            switch (_step)
            {
                case Step.StairApproach:
                    if (WalkToward(_goal, 0.45f, ref input)) Enter(Step.StairDown, 120);
                    return;
                case Step.StairDown:
                    if (!WalkToward(_goal, 0.45f, ref input)) return;
                    if (FindLowerStair(_stairCell, out var next))
                    {
                        _stairCell = next;
                        if (!StandOn(next, out _goal)) { Finish("failed", "descending_stair_blocked", 1); return; }
                        _path.Clear(); _nextPlan = 0;
                        return;
                    }
                    _entryDirection = _stairCell - _stairStart; _entryDirection.y = 0;
                    if (_entryDirection.sqrMagnitude < 4 || _stairStart.y - _stairCell.y < 3)
                    { Finish("failed", "observed_stair_did_not_descend_to_chamber", 1); return; }
                    _entryDirection = Mathf.Abs(_entryDirection.x) > Mathf.Abs(_entryDirection.z)
                        ? new Vector3(Mathf.Sign(_entryDirection.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(_entryDirection.z));
                    if (!FindCoreRune(out _coreCell)) { Finish("failed", "buried_poi_rune_not_streamed_after_descent", 1); return; }
                    _goal = (Vector3)_coreCell + new Vector3(0.5f, 0, 0.5f) - _entryDirection * 3.2f;
                    Capture("physical_stair_descent");
                    Enter(Step.CoreWalk, 100);
                    return;
                case Step.CoreWalk:
                    if (WalkToward(_goal, 0.5f, ref input))
                    {
                        _beforeExcavation = _game.Personal.GroupBy(s => s.Item).ToDictionary(g => g.Key, g => g.Sum(s => s.Count));
                        _mineCells.Clear();
                        var front = _coreCell - Vector3Int.RoundToInt(_entryDirection);
                        _mineCells.Add(front); _mineCells.Add(front + Vector3Int.up);
                        BeginEquip("basic_drill", Step.Excavate);
                    }
                    return;
                case Step.Excavate:
                    if (ExcavateCells(ref input))
                    {
                        if (_excavated < 2) { Finish("failed", "fresh_contact_was_not_actually_excavated", 1); return; }
                        Capture("excavated_contact");
                        BeginEquip("hand_scanner", Step.CoreAim);
                    }
                    return;
                case Step.CoreAim:
                    if (!Aim((Vector3)_coreCell + Vector3.one * 0.5f, ref input)) return;
                    if (!AimCell(out var aimedCore, out _) || aimedCore != _coreCell)
                    { Finish("failed", "core_rune_not_under_real_crosshair", 1); return; }
                    _scan = null; input.PrimaryDown = true;
                    Enter(Step.CoreReply, 8);
                    return;
                case Step.CoreReply:
                    if (_scan == null || Poi("veyl_shape") == null) return;
                    if (_scan.InfoKey != "survey.veyl.shape") { Finish("failed", "core_scan_did_not_request_shape", 1); return; }
                    _coreScan = true;
                    var socketPoi = Poi("veyl_shape");
                    _socketCell = new Vector3Int(Mathf.FloorToInt(_game.SceneX(socketPoi.X)), _coreCell.y,
                        Mathf.FloorToInt(_game.SceneZ(socketPoi.Z)));
                    if (!LoadedSolid(_socketCell + Vector3Int.down) || _game.World.GetShape(_socketCell.x, _socketCell.y - 1, _socketCell.z) != 0)
                    { Finish("failed", "shape_poi_has_no_observed_full_support", 1); return; }
                    _mineCells.Clear(); _mineCells.Add(_socketCell);
                    var socketFront = _socketCell - Vector3Int.RoundToInt(_entryDirection);
                    _mineCells.Add(socketFront); _mineCells.Add(socketFront + Vector3Int.up);
                    BeginEquip("basic_drill", Step.SocketClear);
                    return;
                case Step.SocketClear:
                    if (ExcavateCells(ref input))
                    {
                        _materialItem = FirstItem(k => ItemCount(k) > (_beforeExcavation.TryGetValue(k, out int count) ? count : 0)
                            && ItemKey.Shape(k) == 0 && _game.Content.GetItem(k)?.PlacesBlock is string block
                            && _game.Content.GetBlock(block) is { Shapeable: true, Solid: true });
                        if (string.IsNullOrEmpty(_materialItem)) { Finish("failed", "no_real_excavated_shapeable_material", 1); return; }
                        BeginEquip(_materialItem, Step.OpenForm);
                    }
                    return;
                case Step.OpenForm:
                    if (HotbarActionUi.Instance?.IsOpen != true) { input.Press(InputAction.HotbarAction); return; }
                    if (ClickVisibleLabel(_game.Localizer.Get("ui.hotbar_action.form"))) Enter(Step.ChooseForm, 10);
                    return;
                case Step.ChooseForm:
                    if (ClickVisibleLabel(_game.Localizer.Get("ui.shape.ramp"))) { _craft = null; Enter(Step.FormReply, 10); }
                    return;
                case Step.FormReply:
                    if (_craft == null) return;
                    if (!_craft.Success) { Finish("failed", "normal_form_craft_rejected: " + _craft.Reason, 1); return; }
                    string held = _game.ItemInSlot(_game.SelectedHotbarSlot);
                    if (ItemKey.Base(held) != ItemKey.Base(_materialItem) || ItemKey.Shape(held) != (int)BlockShape.Ramp) return;
                    Capture("form_created_through_ui");
                    Enter(Step.PlaceShape, 15);
                    return;
                case Step.PlaceShape:
                    Vector3 supportTop = (Vector3)_socketCell + new Vector3(0.5f, -0.03f, 0.5f);
                    if (!Aim(supportTop, ref input)) return;
                    if (!AimCell(out var supportCell, out var placeCell) || supportCell != _socketCell + Vector3Int.down || placeCell != _socketCell)
                    { Finish("failed", "real_placement_ray_does_not_hit_socket_support", 1); return; }
                    input.SecondaryDown = true;
                    Enter(Step.Response, 12);
                    return;
                case Step.Response:
                    if (!_shapedPlacement || !_runeResponded || Poi("veyl_return") == null) return;
                    _response = true; Capture("shaped_socket_and_response");
                    _returning = true; _returnIndex = _breadcrumbs.Count - 1;
                    Enter(Step.ReturnWalk, 300);
                    return;
                case Step.ReturnWalk:
                    if (HorizontalDistance(_player.transform.position, DoorPosition()) < 2.8f
                        && Mathf.Abs(_player.transform.position.y - DoorPosition().y) < 2)
                    { _scanFrame = -1; Enter(Step.HomeDoorOpen, 15); return; }
                    while (_returnIndex >= 0 && Vector3.Distance(_player.transform.position, _breadcrumbs[_returnIndex]) < 1.1f) _returnIndex--;
                    if (_returnIndex < 0) { Finish("failed", "return_route_ended_before_owned_hatch", 1); return; }
                    if (WalkToward(_breadcrumbs[_returnIndex], 0.6f, ref input)) { _returnIndex--; _path.Clear(); _nextPlan = 0; }
                    return;
                case Step.HomeDoorOpen:
                    var latest = _doors.FirstOrDefault(d => d.Id == _door.Id);
                    if (latest != null) _door = latest;
                    Aim(DoorPosition() + Vector3.up, ref input);
                    if (_door.Open) { _goal = _start; Enter(Step.HomeEnter, 50); }
                    else if ((_door.Kind == "hinge" || _door.Kind == "wood") && _scanFrame < 0)
                    { input.Press(InputAction.Interact); _scanFrame = Time.frameCount; }
                    return;
                case Step.HomeEnter:
                    if (WalkToward(_goal, 0.7f, ref input)) Enter(Step.HomeReply, 15);
                    return;
                case Step.HomeReply:
                    _home = _game.LandedShips.Values.FirstOrDefault(s => s.OwnerId == _game.LocalPlayerId);
                    if (!_game.VeylSurveyComplete || !_game.Aboard || _lastState?.AboardShip != true
                        || _home?.HasVeylSpecimen != true || !_game.UnlockedBlueprints.Contains("terrain_scanner")) return;
                    if (!InsideOwnHull()) { Finish("failed", "completion_not_inside_owned_hull", 1); return; }
                    if (ItemCount("terrain_scanner") != 1) { Finish("failed", "homecoming_reward_count_is_not_one", 1); return; }
                    _homecoming = true; Capture("physical_homecoming_and_ship_specimen");
                    BeginEquip("terrain_scanner", Step.UseReward);
                    return;
                case Step.UseReward:
                    _energyBeforeUse = _game.SuitEnergy; _oreScan = null;
                    input.SecondaryDown = true;
                    Enter(Step.RewardReply, 10);
                    return;
                case Step.RewardReply:
                    if (_oreScan == null || _game.SuitEnergy >= _energyBeforeUse - 0.5f) return;
                    if (_oreScan.X.Length != _oreScan.Y.Length || _oreScan.X.Length != _oreScan.Z.Length
                        || _oreScan.X.Length != _oreScan.Block.Length || _oreScan.Seconds <= 0)
                    { Finish("failed", "invalid_terrain_scanner_reply", 1); return; }
                    _rewardAction = true;
                    Capture("earned_terrain_scanner_action");
                    Finish("acquired", "ordinary_expedition_and_earned_scanner_action_verified; second_process_reload_required", 0);
                    return;
                case Step.ReloadObserve:
                    _home = _game.LandedShips.Values.FirstOrDefault(s => s.OwnerId == _game.LocalPlayerId);
                    if (!_game.VeylSurveyComplete || _home?.HasVeylSpecimen != true || !_game.UnlockedBlueprints.Contains("terrain_scanner")) return;
                    if (!_game.Aboard || !InsideOwnHull() || _home.StructureId != _acquireEvidence.shipStructureId)
                    { Finish("failed", "reload_did_not_restore_same_owned_home_and_specimen", 1); return; }
                    if (ItemCount("terrain_scanner") != _acquireEvidence.rewardCount)
                    { Finish("failed", "reload_reward_missing_or_duplicated", 1); return; }
                    if (Vector3.Distance(_player.transform.position, _acquireEvidence.lastPosition) > 2)
                    { Finish("failed", "reload_did_not_restore_physically_reached_home_position", 1); return; }
                    if (_stableSince == 0) _stableSince = Time.realtimeSinceStartupAsDouble;
                    if (Time.realtimeSinceStartupAsDouble - _stableSince < 10) return;
                    _reloadVerified = true;
                    Finish("passed", "second_process_restored_same_ship_specimen_and_single_reward_without_duplicate", 0);
                    return;
            }
        }

        private bool InsideOwnHull()
        {
            if (_home == null) return false;
            Vector3 local = _player.transform.position - _game.ScenePos(_home.Origin.X, _home.Origin.Y, _home.Origin.Z);
            return local.x >= 0 && local.x <= _home.Width && local.z >= 0 && local.z <= _home.Length
                && local.y >= 0 && local.y <= _home.Height + 1;
        }

        private bool LoadedSolid(Vector3Int p) => _game.World.TryGetBlock(p.x, p.y, p.z, out var b)
            && _game.Content.BlockById(b) is { Solid: true };

        /// <summary>One ordinary click per server acknowledgement, with the ordinary drill cadence.
        /// A held loop could destroy the rune after the last cover reply is polled later in this frame.</summary>
        private bool ExcavateCells(ref JourneyInputSource.Frame input)
        {
            if (_mineWaiting || Time.realtimeSinceStartupAsDouble < _nextMine) return false;
            var remaining = _mineCells.Where(c => !_game.World.TryGetBlock(c.x, c.y, c.z, out var b) || !b.IsAir).ToList();
            if (remaining.Count == 0) return true;
            remaining.Sort((a, b) => Vector3.Distance(_player.Camera.transform.position, a).CompareTo(Vector3.Distance(_player.Camera.transform.position, b)));
            var target = remaining[0];
            if (!_game.World.TryGetBlock(target.x, target.y, target.z, out var id))
            { Finish("failed", "excavation_cell_not_streamed", 1); return false; }
            if (_game.Content.BlockById(id) is not { Mineable: true } definition || definition.Key == "rune_stone")
            { Finish("failed", "excavation_would_touch_protected_or_unmineable_block", 1); return false; }
            if (!Aim((Vector3)target + Vector3.one * 0.5f, ref input)) return false;
            if (!AimCell(out var hit, out _) || !_mineCells.Contains(hit))
            { Finish("failed", "excavation_ray_blocked_by_unplanned_cell", 1); return false; }
            _pendingMine = hit; _mineWaiting = true; _nextMine = Time.realtimeSinceStartupAsDouble + 0.36;
            input.PrimaryDown = true; // no held edge: the normal tap sends exactly one mine intent
            return false;
        }
    }
}
