// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Opt-in ordinary-survival expedition verification. Publishes controller input and hit-tested uGUI
    /// pointer events; never moves the body or sends gameplay intents. Acquisition and a second-process
    /// reload are separate runs. A requested surface-only prefix exits 2 and never claims completion.</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed partial class SurveyJourneyProbe : MonoBehaviour
    {
        private enum Step { Menu, Loading, DoorApproach, DoorOpen, LeaveHull, Equip, OpenSwap, ChooseSwap,
            WaitSwap, SignalWalk, SignalAim, ScanReply, StairApproach, StairDown, CoreWalk, Excavate,
            CoreAim, CoreReply, SocketClear, OpenForm, ChooseForm, FormReply, PlaceShape, Response,
            ReturnWalk, HomeDoorOpen, HomeEnter, HomeReply, UseReward, RewardReply, ReloadObserve }
        private readonly JourneyInputSource _input = new();
        private readonly List<Vector3> _path = new();
        private readonly List<NetDoor> _doors = new();
        private readonly Dictionary<BaseInputModule, bool> _nativeUiModules = new();
        private EventSystem _isolatedEventSystem;
        private AppShell _shell;
        private GameBootstrap _game;
        private NetworkClient _network;
        private PlayerController _player;
        private CharacterController _capsule;
        private LandedShipModel _home;
        private NetDoor _door;
        private PlayerStateUpdate _lastState;
        private ScanResult _scan;
        private Step _step;
        private string _world, _out, _mode = "acquire", _equipItem;
        private Step _afterEquip;
        private Result _acquireEvidence, _result;
        private bool _surfaceOnly, _previousRunInBackground, _inputIsolationMaintained;
        private int _requestedWidth, _requestedHeight;
        private double _nextStartupSample, _lastStateAt;
        private string _lastShellPhase;
        private long _seed = 4242;
        private double _deadline, _nextSample, _lastProgress, _nextPlan, _quitAt, _globalDeadline, _nextJump, _readySince;
        private Vector3 _start, _lastPosition, _progressPosition, _goal, _doorNormal, _rune;
        private float _walked;
        private int _scannerSlot = -1, _vantage, _scanFrame = -1, _exitCode;
        private bool _attached, _finished, _returnedToMenu, _physicalExit, _surfaceScan;
        private StreamWriter _events;
        private string _lastScanInfo;

        [Serializable]
        private sealed class Record
        {
            public string kind, step, detail, selectedItem, shellPhase, graphicsApi, fullscreenMode;
            public double seconds, scaledSeconds, authoritativeStateAgeSeconds;
            public int frame, actualWidth, actualHeight, requestedWidth, requestedHeight, targetFrameRate, vSyncCount;
            public float deltaTime, unscaledDeltaTime, maximumDeltaTime, timeScale;
            public bool focused, runInBackground, inputIsolated, nativeUiInputSuspended;
            public Vector3 position, authoritativePosition, navigationGoal, nextWaypoint, hatchPosition;
            public int waypointCount;
            public Vector2 move, look;
            public bool aboard, primaryDown, primaryHeld, secondaryDown, jumpDown;
            public int slotOneBased;
            public string actionsDown;
        }

        [Serializable]
        private sealed class Result
        {
            public string status, checkpoint, reason, world, mode, shipStructureId, acquireEvidencePath;
            public string shellPhase, evidenceScope, graphicsApi, fullscreenMode;
            public int actualWidth, actualHeight, requestedWidth, requestedHeight, finalFrame;
            public double elapsedSeconds, scaledSeconds;
            public bool focused, runInBackground, inputIsolationVerified;
            public string inputIsolationScope = "InputMap gameplay and current EventSystem native modules; native Escape/cancel aborts";
            public long seed;
            public int exitCode, processId, rewardCount, blocksExcavated, oreHits;
            public bool journeyComplete, physicalHatchExit, surfaceScan, coreScan, shapedPlacement, response,
                ownShipHomecoming, rewardAction, shutdownGraceful, reloadVerified;
            public float distanceWalked;
            public Vector3 spawn, lastPosition;
            public string[] remaining = { "excavation", "shape UI and placement", "response", "own-ship homecoming", "reward action", "second-process reload" };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-verifySurveyJourney")) return;
            var go = new GameObject("Survey journey verification");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<SurveyJourneyProbe>();
            probe._previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            probe._out = Argument(args, "-journeyOut");
            int.TryParse(Argument(args, "-screen-width"), out probe._requestedWidth);
            int.TryParse(Argument(args, "-screen-height"), out probe._requestedHeight);
            probe._mode = Argument(args, "-journeyMode") ?? "acquire";
            probe._surfaceOnly = Argument(args, "-journeyCheckpoint") == "surface";
            string seed = Argument(args, "-journeySeed");
            if (!string.IsNullOrEmpty(seed) && long.TryParse(seed, out long value)) probe._seed = value;
            // No existing save can be silently reused by the acquisition prefix.
            probe._world = "Journey_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrEmpty(probe._out) || !Path.IsPathRooted(probe._out)
                || (probe._mode != "acquire" && probe._mode != "reload"))
            {
                Debug.LogError("[Journey] Require absolute -journeyOut; -journeyMode must be acquire or reload.");
                Application.Quit(1);
                Destroy(go);
                return;
            }
            Directory.CreateDirectory(probe._out);
            if (File.Exists(Path.Combine(probe._out, "events.ndjson")) || File.Exists(Path.Combine(probe._out, "result.json")))
            {
                Debug.LogError("[Journey] Output already contains a run; choose a fresh directory.");
                Application.Quit(1);
                Destroy(go);
                return;
            }
            if (probe._mode == "reload")
            {
                probe._acquireEvidencePath = Argument(args, "-journeyAcquireResult");
                try
                {
                    if (string.IsNullOrEmpty(probe._acquireEvidencePath) || !Path.IsPathRooted(probe._acquireEvidencePath))
                        throw new InvalidOperationException("Reload requires an absolute acquisition result path.");
                    probe._acquireEvidence = JsonUtility.FromJson<Result>(File.ReadAllText(probe._acquireEvidencePath));
                    var evidence = probe._acquireEvidence;
                    if (evidence.status != "acquired" || !evidence.inputIsolationVerified || !evidence.shutdownGraceful || !evidence.rewardAction
                        || evidence.rewardCount != 1 || !evidence.ownShipHomecoming || !evidence.shapedPlacement
                        || evidence.processId == System.Diagnostics.Process.GetCurrentProcess().Id
                        || string.IsNullOrEmpty(evidence.world) || !evidence.world.StartsWith("Journey_", StringComparison.Ordinal)
                        || evidence.world.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        throw new InvalidOperationException("A successful, gracefully saved acquisition in another process is required.");
                    probe._world = evidence.world;
                    probe._seed = evidence.seed;
                }
                catch (Exception error)
                {
                    Debug.LogError("[Journey] Invalid reload evidence: " + error.Message);
                    Application.Quit(1); Destroy(go); return;
                }
            }
            probe._events = new StreamWriter(Path.Combine(probe._out, "events.ndjson"), append: false) { AutoFlush = true };
            // A fresh profile plays the normal intro. Its timeline uses scaled frame time, so allow
            // generous wall time on slow/background rendering without skipping or modifying it.
            probe._deadline = Time.realtimeSinceStartupAsDouble + 300;
            probe._globalDeadline = Time.realtimeSinceStartupAsDouble + 1200;
            probe._attached = InputMap.AttachVerificationInput(probe._input);
            probe._inputIsolationMaintained = probe._attached;
            if (!probe._attached) probe.Finish("failed", "verification_input_not_attached", 1);
        }

        private static string Argument(string[] args, string key)
        {
            int at = Array.IndexOf(args, key);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        private void Update()
        {
            var frame = new JourneyInputSource.Frame();
            if (_finished)
            {
                if (Time.realtimeSinceStartupAsDouble >= _quitAt)
                {
                    if (!_returnedToMenu)
                    {
                        _returnedToMenu = true;
                        _shell?.ReturnToMenu(); // ordinary disconnect + graceful bundled-server stop
                        ReleaseInput(); // hold neutral controls until the final pose has been saved
                        if (_result != null)
                        {
                            _result.shutdownGraceful = LocalServerLauncher.LastStopWasGraceful == true;
                            if (_exitCode == 0 && !_result.shutdownGraceful)
                            {
                                _result.status = "failed"; _result.reason = "bundled_server_did_not_exit_cleanly";
                                _result.journeyComplete = false; _result.exitCode = _exitCode = 1;
                            }
                            WriteResult();
                        }
                        _quitAt = Time.realtimeSinceStartupAsDouble + 1;
                    }
                    else Application.Quit(_exitCode);
                }
                return;
            }
            try
            {
                if (!InputMap.OwnsVerificationInput(_input))
                {
                    _inputIsolationMaintained = false;
                    Finish("failed", "exclusive_input_lease_lost", 1);
                    return;
                }
                SuspendNativeUiInput();
                // Keep an ordinary user cancellation route; do not capture or suppress OS input.
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton1))
                {
                    Finish("failed", "user_cancelled_journey", 1);
                    return;
                }
                _shell ??= FindAnyObjectByType<AppShell>();
                ObserveBoot();
                string shellPhase = _shell != null ? _shell.Phase.ToString() : "not_found";
                if (_lastShellPhase != shellPhase)
                {
                    _lastShellPhase = shellPhase;
                    Log("shell_phase", shellPhase);
                }
                if (_step <= Step.Loading && Time.realtimeSinceStartupAsDouble >= _nextStartupSample)
                {
                    _nextStartupSample = Time.realtimeSinceStartupAsDouble + 1;
                    Log("startup_sample", _game == null ? "world_not_started" : "world_loading");
                }
                if (Time.realtimeSinceStartupAsDouble > _globalDeadline) { Finish("failed", "journey_total_timeout", 1); return; }
                if (Time.realtimeSinceStartupAsDouble > _deadline)
                {
                    Finish("failed", _step == Step.Menu ? "startup_timeout_before_world: " + shellPhase : "checkpoint_timeout", 1);
                    return;
                }
                if (InputMap.ScriptedMove != Vector2.zero) { Finish("failed", "other_scripted_movement_active", 1); return; }
                if (_step > Step.Loading && _game != null && (_game.Health <= 0 || _game.AwaitingRespawnConfirm))
                { Finish("failed", "player_died_or_requires_respawn", 1); return; }
                Drive(ref frame);
                if (!_finished)
                {
                    _input.Publish(frame);
                    if (frame.PrimaryDown || frame.SecondaryDown || frame.JumpDown || frame.ActionsDown != 0 || frame.SlotOneBased > 0)
                        Log("input", null, frame);
                }
                if (_player != null && _step > Step.Loading)
                {
                    Vector3 current = _player.transform.position;
                    float moved = Vector3.Distance(current, _lastPosition);
                    if (moved > Mathf.Max(3f, 20f * Time.unscaledDeltaTime))
                    { Finish("failed", "unexpected_position_discontinuity", 1); return; }
                    _walked += moved;
                    _lastPosition = current;
                    if (!_returning && (_breadcrumbs.Count == 0 || Vector3.Distance(current, _breadcrumbs[_breadcrumbs.Count - 1]) >= 1f))
                        _breadcrumbs.Add(current);
                    if (Time.realtimeSinceStartupAsDouble >= _nextSample)
                    {
                        _nextSample = Time.realtimeSinceStartupAsDouble + 0.25;
                        Log("trajectory", null, frame);
                    }
                }
            }
            catch (Exception error) { Finish("failed", error.GetType().Name + ": " + error.Message, 1); }
        }

        private void ObserveBoot()
        {
            if (_shell?.CurrentBoot == null) return;
            _game = _shell.CurrentBoot;
            if (_network != _game.Network && _game.Network != null)
            {
                _network = _game.Network;
                _network.DoorsReceived += m => { _doors.Clear(); _doors.AddRange(m.Doors); Log("doors", m.Doors.Length.ToString()); };
                _network.PlayerStateUpdated += m =>
                {
                    if (m.PlayerId != _game.LocalPlayerId) return;
                    _lastState = m; _lastStateAt = Time.realtimeSinceStartupAsDouble;
                };
                _network.ScanResultReceived += m => { _scan = m; _lastScanInfo = m.InfoKey; Log("scan_reply", m.SubjectKey + "/" + m.InfoKey); };
                _network.ActionRejected += m =>
                {
                    Log("action_rejected", m.Action + ": " + m.Reason);
                    if (_step > Step.Loading) Finish("failed", "authoritative_action_rejected: " + m.Action + ": " + m.Reason, 1);
                };
                _network.InventoryUpdated += m => Log("inventory_reply", string.Join(",", m.Personal.Select(s => s.Slot + ":" + s.Item + "=" + s.Count)));
                ObserveActions();
            }
        }

        private void Drive(ref JourneyInputSource.Frame input)
        {
            switch (_step)
            {
                case Step.Menu:
                    if (_shell?.Phase != ShellPhase.MainMenu) return;
                    bool exists = LocalServerLauncher.ListWorlds().Contains(_world);
                    if ((_mode == "acquire" && exists) || (_mode == "reload" && !exists))
                    { Finish("failed", _mode == "reload" ? "acquired_world_missing" : "fresh_world_name_collision", 1); return; }
                    _shell.StartSingleplayerWorld(_world, _seed, worldOptions: new WorldCreationOptions { StartPlanetType = "rocky" });
                    Enter(Step.Loading, 240);
                    return;
                case Step.Loading:
                    if (_game == null || !_game.WorldReady || string.IsNullOrEmpty(_game.LocalPlayerId)) return;
                    _player = FindObjectsByType<PlayerController>().FirstOrDefault(p => p.Game == _game);
                    if (_player == null || _player.Camera == null) return;
                    _capsule = _player.GetComponent<CharacterController>();
                    if (!_capsule.enabled || _game.MenuOpen || _game.CinematicCameraActive || _game.VegaPrologueActive)
                    { _readySince = 0; return; }
                    if (_readySince == 0) _readySince = Time.realtimeSinceStartupAsDouble;
                    if (Time.realtimeSinceStartupAsDouble - _readySince < 2) return;
                    _home = _game.LandedShips.Values.FirstOrDefault(s => s.OwnerId == _game.LocalPlayerId);
                    if (_home == null || !_game.Aboard || _game.Story == null || _doors.Count == 0) return;
                    if (_mode == "reload")
                    {
                        _start = _lastPosition = _progressPosition = _player.transform.position;
                        if (_game.CanFly || _game.Story.StoryId != "voidcraft_awakening")
                        { Finish("failed", "reload_is_not_the_ordinary_survey_world", 1); return; }
                        Enter(Step.ReloadObserve, 30); return;
                    }
                    if (_game.CanFly || _game.VeylSurveyComplete || _game.Story.StoryId != "voidcraft_awakening")
                    { Finish("failed", "ordinary_fresh_veyl_world_required", 1); return; }
                    if (Poi("veyl_signal") == null) return; // the last join packets can trail the first ready frame
                    _start = _lastPosition = _progressPosition = _player.transform.position;
                    _lastProgress = Time.realtimeSinceStartupAsDouble;
                    if (!ChooseHatch()) { Finish("failed", "no_observed_own_hull_exit_door", 1); return; }
                    _goal = DoorPosition() - _doorNormal * 1.3f;
                    Enter(Step.DoorApproach, 75);
                    return;
                case Step.DoorApproach:
                    if (WalkToward(_goal, 0.7f, ref input)) Enter(Step.DoorOpen, 12);
                    return;
                case Step.DoorOpen:
                    var latestDoor = _doors.FirstOrDefault(d => d.Id == _door.Id);
                    if (latestDoor != null) _door = latestDoor;
                    Aim(DoorPosition() + Vector3.up, ref input);
                    if (_door.Open)
                    {
                        var origin = _game.ScenePos(_home.Origin.X, _home.Origin.Y, _home.Origin.Z);
                        _goal = DoorPosition();
                        if (_doorNormal.x != 0) _goal.x = origin.x + (_doorNormal.x > 0 ? _home.Width + 2 : -2);
                        else _goal.z = origin.z + (_doorNormal.z > 0 ? _home.Length + 2 : -2);
                        Enter(Step.LeaveHull, 45);
                    }
                    else if ((_door.Kind == "hinge" || _door.Kind == "wood") && _scanFrame < 0)
                    { input.Press(InputAction.Interact); _scanFrame = Time.frameCount; }
                    return;
                case Step.LeaveHull:
                    if (WalkToward(_goal, 0.7f, ref input) && !_game.Aboard && _lastState is { AboardShip: false })
                    {
                        if (_walked < 2f || Vector3.Distance(_start, _player.transform.position) < 2f)
                        { Finish("failed", "exit_without_measured_locomotion", 1); return; }
                        _physicalExit = true;
                        Capture("physical_hatch_exit");
                        BeginEquip("hand_scanner", Step.SignalWalk);
                    }
                    return;
                case Step.Equip:
                    _scannerSlot = Enumerable.Range(0, 24).Where(i => _game.ItemInSlot(i) == _equipItem).DefaultIfEmpty(-1).First();
                    if (_scannerSlot < 0) { Finish("failed", "ordinary_inventory_item_missing: " + _equipItem, 1); return; }
                    if (_scannerSlot < 9)
                    {
                        input.SlotOneBased = _scannerSlot + 1;
                        if (_game.SelectedHotbarSlot == _scannerSlot) FinishEquip();
                    }
                    else
                    {
                        input.SlotOneBased = 9;
                        if (_game.SelectedHotbarSlot == 8) { input.Press(InputAction.HotbarAction); Enter(Step.OpenSwap, 8); }
                    }
                    return;
                case Step.OpenSwap:
                    if (ClickVisibleLabel(_game.Localizer.Get("ui.hotbar_action.swap"))) Enter(Step.ChooseSwap, 8);
                    return;
                case Step.ChooseSwap:
                    string itemLabel = BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(_game.Localizer, _equipItem,
                        idx => _game.CustomShapes?.NameOf(idx));
                    if (itemLabel.Length > 12) itemLabel = itemLabel.Substring(0, 11) + "…";
                    if (ClickVisibleLabel(itemLabel)) Enter(Step.WaitSwap, 8);
                    return;
                case Step.WaitSwap:
                    if (_game.ItemInSlot(8) == _equipItem && HotbarActionUi.Instance?.IsOpen != true) FinishEquip();
                    return;
                case Step.SignalWalk:
                    if (WalkToward(_goal, 0.65f, ref input, matchHeight: false)) Enter(Step.SignalAim, 8);
                    return;
                case Step.SignalAim:
                    if (!FindVisibleRune(out _rune))
                    {
                        if (++_vantage >= 8) { Finish("failed", "no_visible_reachable_signal_rune", 1); return; }
                        BeginSignalWalk();
                        return;
                    }
                    if (Aim(_rune, ref input))
                    {
                        _scan = null;
                        input.PrimaryDown = input.PrimaryHeld = true;
                        _scanFrame = Time.frameCount;
                        Capture("aimed_surface_scan");
                        Enter(Step.ScanReply, 6);
                    }
                    return;
                case Step.ScanReply:
                    if (_scan == null || Time.frameCount <= _scanFrame) return;
                    if (Poi("veyl_excavate") != null && _scan.InfoKey == "survey.veyl.excavate")
                    {
                        _surfaceScan = true;
                        if (_surfaceOnly) Finish("partial", "requested_physical_exit_and_surface_scan_prefix_verified", 2);
                        else BeginDescent();
                    }
                    else Finish("failed", "scan_did_not_advance_signal: " + _scan.SubjectKey + "/" + _lastScanInfo, 1);
                    return;
                default:
                    DriveExpedition(ref input);
                    return;
            }
        }

        private NetPoi Poi(string type) => _game?.PlanetPois?.FirstOrDefault(p => p.Type == type);
        private Vector3 DoorPosition() => _game.ScenePos(_door.X, _door.Y, _door.Z);
        private bool ChooseHatch()
        {
            var origin = _game.ScenePos(_home.Origin.X, _home.Origin.Y, _home.Origin.Z);
            float best = float.PositiveInfinity;
            foreach (var door in _doors)
            {
                Vector3 p = _game.ScenePos(door.X, door.Y, door.Z), local = p - origin;
                if (local.x < -1 || local.x > _home.Width + 1 || local.z < -1 || local.z > _home.Length + 1
                    || local.y < 0 || local.y > _home.Height) continue;
                float edge = door.AxisX ? Mathf.Min(local.z, _home.Length - local.z) : Mathf.Min(local.x, _home.Width - local.x);
                if (edge > 2.5f) continue; // an internal cabin door is not an exit
                float score = Vector3.Distance(p, _player.transform.position);
                if (score >= best) continue;
                best = score; _door = door;
                _doorNormal = door.AxisX ? new Vector3(0, 0, local.z < _home.Length / 2f ? -1 : 1)
                    : new Vector3(local.x < _home.Width / 2f ? -1 : 1, 0, 0);
            }
            return _door != null;
        }

        private void BeginSignalWalk()
        {
            var poi = Poi("veyl_signal");
            if (poi == null) { Finish("failed", "signal_disappeared_before_scan", 1); return; }
            Vector3 center = _game.ScenePos(poi.X, _player.transform.position.y, poi.Z);
            float angle = Mathf.Atan2(_start.x - center.x, _start.z - center.z) + _vantage * Mathf.PI / 4f;
            _goal = center + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 3.2f;
            Enter(Step.SignalWalk, 150);
        }

        private bool Aim(Vector3 target, ref JourneyInputSource.Frame input)
        {
            Vector3 direction = target - _player.Camera.transform.position;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            float dy = Mathf.DeltaAngle(_player.transform.eulerAngles.y, yaw);
            float dp = Mathf.DeltaAngle(_player.Camera.transform.localEulerAngles.x, pitch);
            float rate = 110f * Time.unscaledDeltaTime, sensitivity = Mathf.Max(0.01f, _player.MouseSensitivity);
            input.Look.x = Mathf.Clamp(dy, -rate, rate) / sensitivity;
            input.Look.y = -Mathf.Clamp(dp, -rate, rate) / sensitivity * (_player.InvertY ? -1f : 1f);
            return Mathf.Abs(dy) < 1.5f && Mathf.Abs(dp) < 1.5f;
        }

        private bool WalkToward(Vector3 goal, float reach, ref JourneyInputSource.Frame input, bool matchHeight = true)
        {
            if (_game.MenuOpen) return false; // modal input must never be forced through the controller
            Vector3 pos = _player.transform.position;
            if (HorizontalDistance(pos, goal) <= reach && (!matchHeight || Mathf.Abs(pos.y - goal.y) < 1.25f)) return true;
            if (Vector3.Distance(pos, _progressPosition) > 0.2f)
            { _progressPosition = pos; _lastProgress = Time.realtimeSinceStartupAsDouble; }
            if (Time.realtimeSinceStartupAsDouble - _lastProgress > 12)
            { Finish("failed", "physical_navigation_stalled", 1); return false; }
            while (_path.Count > 0 && HorizontalDistance(pos, _path[0]) < 0.4f && Mathf.Abs(pos.y - _path[0].y) < 1.25f) _path.RemoveAt(0);
            if ((_path.Count == 0 || Time.realtimeSinceStartupAsDouble - _lastProgress > 3) && Time.realtimeSinceStartupAsDouble >= _nextPlan)
            {
                _nextPlan = Time.realtimeSinceStartupAsDouble + 1.5;
                _path.Clear();
                _path.AddRange(JourneyWalkPlanner.Plan(_player, _capsule, goal));
                Log("navigation_plan", "waypoints=" + _path.Count);
            }
            if (_path.Count == 0) return false;
            Vector3 target = _path[0];
            Aim(new Vector3(target.x, _player.Camera.transform.position.y, target.z), ref input);
            float yaw = Mathf.Atan2(target.x - pos.x, target.z - pos.z) * Mathf.Rad2Deg;
            float error = Mathf.Abs(Mathf.DeltaAngle(_player.transform.eulerAngles.y, yaw));
            if (error < 22f)
            {
                input.Move.y = Mathf.Clamp01(HorizontalDistance(pos, target) / 0.8f);
                if (target.y - pos.y > _capsule.stepOffset + 0.05f && _capsule.isGrounded
                    && Time.realtimeSinceStartupAsDouble >= _nextJump)
                {
                    input.JumpDown = input.JumpHeld = true;
                    _nextJump = Time.realtimeSinceStartupAsDouble + 0.9;
                }
            }
            return false;
        }

        internal static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private bool FindVisibleRune(out Vector3 point)
        {
            point = default;
            var poi = Poi("veyl_signal");
            if (poi == null) return false;
            Vector3 center = _game.ScenePos(poi.X, _player.transform.position.y, poi.Z);
            Vector3 eye = _player.Camera.transform.position;
            for (int x = Mathf.FloorToInt(center.x) - 1; x <= Mathf.FloorToInt(center.x) + 1; x++)
            for (int z = Mathf.FloorToInt(center.z) - 1; z <= Mathf.FloorToInt(center.z) + 1; z++)
            for (int y = Mathf.FloorToInt(center.y) - 2; y <= Mathf.FloorToInt(center.y) + 8; y++)
            {
                if (_game.Content.BlockById(_game.World.GetBlock(x, y, z))?.Key != "rune_stone") continue;
                Vector3 target = new(x + 0.5f, y + 0.5f, z + 0.5f);
                float distance = Vector3.Distance(eye, target);
                if (distance > 5.5f) continue;
                bool visible = true;
                for (float t = 0.1f; t < distance; t += 0.05f)
                {
                    Vector3 p = Vector3.Lerp(eye, target, t / distance);
                    int bx = Mathf.FloorToInt(p.x), by = Mathf.FloorToInt(p.y), bz = Mathf.FloorToInt(p.z);
                    if (bx == x && by == y && bz == z) break;
                    if (!_game.World.TryGetBlock(bx, by, bz, out var through) || !through.IsAir) { visible = false; break; }
                }
                if (visible) { point = target; return true; }
            }
            return false;
        }

        // Real uGUI hit testing and pointer handlers; never Button.onClick.Invoke or direct craft/swap RPCs.
        private bool ClickVisibleLabel(string label)
        {
            if (EventSystem.current == null || string.IsNullOrEmpty(label)) return false;
            foreach (var text in FindObjectsByType<Text>())
            {
                if (text.text != label || !text.isActiveAndEnabled) continue;
                var button = text.GetComponentInParent<Button>();
                if (button == null || !button.IsInteractable()) continue;
                var canvas = text.GetComponentInParent<Canvas>();
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(text.rectTransform.rect.center));
                var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(data, hits);
                if (hits.Count == 0 || ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) != button.gameObject) continue;
                data.pointerCurrentRaycast = hits[0];
                data.pressPosition = point;
                data.pointerPressRaycast = hits[0];
                data.eligibleForClick = true;
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
                Log("ui_pointer_click", label + " at " + point);
                return true;
            }
            return false;
        }

        private void Enter(Step step, double seconds)
        {
            _step = step;
            _deadline = Time.realtimeSinceStartupAsDouble + seconds;
            _path.Clear(); _nextPlan = 0;
            _progressPosition = _player != null ? _player.transform.position : default;
            _lastProgress = Time.realtimeSinceStartupAsDouble;
            Log("checkpoint_enter", null);
        }

        private void Log(string kind, string detail, JourneyInputSource.Frame frame = default)
        {
            _events?.WriteLine(JsonUtility.ToJson(new Record
            {
                kind = kind, step = _step.ToString(), detail = detail, seconds = Time.realtimeSinceStartupAsDouble,
                shellPhase = _shell != null ? _shell.Phase.ToString() : "not_found",
                scaledSeconds = Time.timeAsDouble, frame = Time.frameCount,
                deltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime,
                maximumDeltaTime = Time.maximumDeltaTime, timeScale = Time.timeScale,
                focused = Application.isFocused, runInBackground = Application.runInBackground,
                inputIsolated = InputMap.OwnsVerificationInput(_input),
                nativeUiInputSuspended = _nativeUiModules.Count > 0,
                actualWidth = Screen.width, actualHeight = Screen.height,
                requestedWidth = _requestedWidth, requestedHeight = _requestedHeight,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), fullscreenMode = Screen.fullScreenMode.ToString(),
                targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount,
                position = _player != null ? _player.transform.position : default,
                authoritativePosition = _lastState == null ? default : new Vector3(_lastState.X, _lastState.Y, _lastState.Z),
                // This packet is changed-only (vitals/aboard), not a continuous movement echo.
                authoritativeStateAgeSeconds = _lastState == null ? -1 : Time.realtimeSinceStartupAsDouble - _lastStateAt,
                navigationGoal = _goal, nextWaypoint = _path.Count == 0 ? default : _path[0], waypointCount = _path.Count,
                hatchPosition = _door == null ? default : DoorPosition(),
                selectedItem = _game?.ItemInSlot(_game.SelectedHotbarSlot), aboard = _game != null && _game.Aboard,
                move = frame.Move, look = frame.Look, primaryDown = frame.PrimaryDown, primaryHeld = frame.PrimaryHeld,
                secondaryDown = frame.SecondaryDown, jumpDown = frame.JumpDown, slotOneBased = frame.SlotOneBased,
                actionsDown = frame.ActionsDown.ToString()
            }));
        }

        private void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(_out, name + ".png"));
        private void Finish(string status, string reason, int code)
        {
            if (_finished) return;
            _finished = true; _exitCode = code; _quitAt = Time.realtimeSinceStartupAsDouble + 1.5;
            _input.Clear(); // retain the exclusive neutral lease through graceful disconnect/save
            Log("finished", status + ": " + reason);
            _events?.Flush();
            if (!string.IsNullOrEmpty(_out))
            {
                _result = new Result
                {
                    status = status, checkpoint = _step.ToString(), reason = reason, world = _world, seed = _seed,
                    mode = _mode, processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    shellPhase = _shell != null ? _shell.Phase.ToString() : "not_found",
                    evidenceScope = _step == Step.Menu ? "startup_only" : _step == Step.Loading ? "world_loading" : "in_world_journey",
                    elapsedSeconds = Time.realtimeSinceStartupAsDouble, scaledSeconds = Time.timeAsDouble, finalFrame = Time.frameCount,
                    focused = Application.isFocused, runInBackground = Application.runInBackground,
                    inputIsolationVerified = _inputIsolationMaintained,
                    actualWidth = Screen.width, actualHeight = Screen.height,
                    requestedWidth = _requestedWidth, requestedHeight = _requestedHeight,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(), fullscreenMode = Screen.fullScreenMode.ToString(),
                    exitCode = code, journeyComplete = _reloadVerified && code == 0,
                    physicalHatchExit = _physicalExit || _acquireEvidence?.physicalHatchExit == true,
                    surfaceScan = _surfaceScan || _acquireEvidence?.surfaceScan == true,
                    coreScan = _coreScan || _acquireEvidence?.coreScan == true,
                    shapedPlacement = _shapedPlacement || _acquireEvidence?.shapedPlacement == true,
                    response = _response || _acquireEvidence?.response == true,
                    ownShipHomecoming = _homecoming || _acquireEvidence?.ownShipHomecoming == true,
                    rewardAction = _rewardAction || _acquireEvidence?.rewardAction == true, reloadVerified = _reloadVerified,
                    shipStructureId = _home?.StructureId, acquireEvidencePath = _acquireEvidencePath,
                    blocksExcavated = _excavated, rewardCount = ItemCount("terrain_scanner"), oreHits = _oreScan?.X.Length ?? 0,
                    remaining = code == 0 ? (_reloadVerified ? Array.Empty<string>() : new[] { "second-process reload" })
                        : new[] { "journey stopped at " + _step },
                    distanceWalked = _walked, spawn = _start, lastPosition = _player != null ? _player.transform.position : default
                };
                WriteResult();
                Capture(status + "_" + _step);
            }
            Debug.Log("[Journey] " + status + ": " + reason + " (exit " + code + ")");
        }

        private void WriteResult()
            => File.WriteAllText(Path.Combine(_out, "result.json"), JsonUtility.ToJson(_result, true));

        private void SuspendNativeUiInput()
        {
            // The driver already raycasts actual uGUI controls and sends pointer down/up/click through
            // ExecuteEvents. Suppress competing native pointer/navigation events, preserving the same
            // raycasters, interactable checks and button handlers. This is opt-in only, never OS input.
            var current = EventSystem.current;
            if (current != null && current != _isolatedEventSystem)
            {
                _isolatedEventSystem = current;
                foreach (var module in current.GetComponents<BaseInputModule>())
                    if (!_nativeUiModules.ContainsKey(module)) _nativeUiModules.Add(module, module.enabled);
            }
            foreach (var pair in _nativeUiModules)
                if (pair.Key != null) pair.Key.enabled = false;
        }

        private void ReleaseInput()
        {
            _input.Clear();
            if (_attached) InputMap.DetachVerificationInput(_input);
            _attached = false;
            foreach (var pair in _nativeUiModules)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _nativeUiModules.Clear();
            _isolatedEventSystem = null;
        }

        private void OnApplicationQuit() => ReleaseInput();

        private void OnDestroy()
        {
            ReleaseInput();
            _events?.Dispose();
            Application.runInBackground = _previousRunInBackground;
        }
    }
}
