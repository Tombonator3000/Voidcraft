// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Automated performance baseline capture (issue #353). When the player is launched with
    /// <c>-perfProbe</c>, this self-installs (same pattern as <see cref="ScreenshotDirector"/>), starts a
    /// fixed-seed singleplayer world and records frame-time / GC statistics over these phases:
    /// <list type="number">
    ///   <item><b>idle</b> — standing still after the spawn area has fully meshed (steady-state cost)</item>
    ///   <item><b>spawn_forward_input</b> — scripted input, with measured position/aboard state; a wall may stop it.
    ///   <c>-perfTerrain</c> first poses the player on safe nearby terrain and validates actual traversal.</item>
    ///   <item><b>dense</b> (only with <c>-perfDense</c>) — Extreme creature abundance at forced visual midnight,
    ///   so the glowing-entity point-light cost (#361) is exercised instead of the sparse baseline walk</item>
    /// </list>
    /// Results go to <c>&lt;out&gt;/perf_baseline_&lt;platform&gt;.json</c> plus a human-readable .txt and the
    /// log; the process exits when done, so a script can run this end-to-end. Flags: <c>-perfProbe</c>,
    /// <c>-perfOut &lt;dir&gt;</c>, <c>-seed &lt;n&gt;</c>, <c>-perfIdle &lt;sec&gt;</c>, <c>-perfWalk &lt;sec&gt;</c>,
    /// <c>-perfPreset &lt;name&gt;</c>, <c>-perfVd &lt;n&gt;</c>, and <c>-perfFeature "ssao=off|half|full,depth=off,
    /// smaa=off,scatter=off,pom=on|off,shadowmap=2048,shadowdist=40"</c> (isolate one preset feature's cost — see #374).
    /// The numbers are a coarse CPU/GC baseline (wall-clock frame times), not a GPU profile — for the deep
    /// dive attach the Unity Profiler to a development build. When the preset is GPU-bound (the Medium cliff
    /// in #374), the wall-clock frame time still moves with each feature toggle, so a <c>-perfFeature</c>
    /// sweep at fixed preset/VD gives a usable first-order cost split without a GPU capture.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class PerfProbe : MonoBehaviour
    {
        private const string WorldName = "PerfProbe";
        private const long DefaultSeed = 424242L;      // same reproducible world the marketing shots use
        private const float WorldLoadTimeout = 120f;
        private const float ChunkReadyTimeout = 120f; // explicit deadline, never assume elapsed time means ready
        private const float ChunkQuietSeconds = 2f;  // queues empty and cinematic/prologue released continuously
        private const float FixedCameraPositionTolerance = 0.02f;
        private const float FixedCameraAngleTolerance = 0.1f;
        private const float FixedCameraFovTolerance = 0.1f;
        private const float HitchMs33 = 1000f / 30f;   // frame longer than a 30 FPS frame
        private const float HitchMs100 = 100f;         // a visible stall

        private long _seed = DefaultSeed;
        private string _outDir;
        private float _idleSeconds = 30f;
        private float _walkSeconds = 60f;
        private string _presetOverride;   // -perfPreset Potato|Low|Medium|High; null = keep the player's settings
        private int _vdOverride = -1;     // -perfVd 1..8; -1 = keep

        // -perfFeature "ssao=off,depth=off,smaa=off,scatter=off,pom=on|off,shadowmap=2048,shadowdist=40": after the preset
        // is applied, force individual cost-bearing features off (or to a value) so a run isolates ONE feature's
        // frame-time contribution. This is how the Medium-preset cost split (#374) gets itemized: hold the preset
        // at Medium and toggle one feature per run. Null/empty = no per-feature override.
        private string _featureSpec;
        private string _pomRequested;
        private bool _pomApplied;
        private float _pomAppliedScale = -1f;
        private string _featureTag;       // sanitized summary of what the override actually changed (for the filename)

        // -perfDense: adds a third "dense" phase (settlement/creature-pack-at-night stand-in for #361). The world
        // is created with Extreme creature abundance on a breathable planet so fauna auto-spawns in the 18–45 block
        // ring around the player; the phase then forces visual midnight (SetCaptureEnvironment) so the glowing
        // entities' point lights dominate a dark frame. Caveat: this pins the CLIENT visual clock only — the server
        // clock stays daytime, so strictly nocturnal glow species may be under-represented; cathemeral/passive
        // species and the raw entity-view density still populate the scene. A first dense probe, refine later.
        private bool _dense;
        private bool _terrain; // explicit, pose-prepared terrain probe; never a new-player journey
        private GameBootstrap _boot;
        private bool _terrainPrepared;
        private readonly List<ReadinessResult> _readiness = new List<ReadinessResult>();
        private bool _lastReadinessPassed;
        private string _measurementFailure;
        private JourneyInputSource _input;
        private Vector2 _probeMove;
        private bool _inputAttached;
        private ProfilerRecorder _drawCalls, _setPassCalls, _vertices;

        private void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        }

        private void OnDisable()
        {
            _drawCalls.Dispose();
            _setPassCalls.Dispose();
            _vertices.Dispose();
            ReleaseInput();
        }

        private void Update()
        {
            if (_inputAttached) _input.Publish(new JourneyInputSource.Frame { Move = _probeMove });
        }

        private void ReleaseInput()
        {
            _probeMove = Vector2.zero;
            _input?.Clear();
            if (_inputAttached)
            {
                BlockParallaxController.ReleasePerformanceOverride(_input);
                InputMap.DetachVerificationInput(_input);
            }
            _inputAttached = false;
        }

        private const float DenseSettle = 12f; // extra settle so the creature ring fills toward its cap before sampling
        private const string DensePlanet = "jungle"; // breathable + vegetated ⇒ dense fauna (incl. glowers)

        // The player's real settings file, snapshotted before any override — AppShell paths call
        // Settings.Save(), so an in-memory override could otherwise clobber the user's persisted settings.
        private byte[] _settingsBackup;
        private bool _settingsExisted;
        private bool _didBackup; // restore only when a backup was actually taken (i.e. an override ran)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            var args = Environment.GetCommandLineArgs();
            bool on = false;
            long seed = DefaultSeed;
            string outDir = null;
            float idle = 30f, walk = 60f;
            string preset = null;
            int vd = -1;
            string feature = null;
            bool dense = false;
            bool terrain = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-perfProbe", StringComparison.OrdinalIgnoreCase))
                {
                    on = true;
                }
                else if (string.Equals(a, "-perfPreset", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    preset = args[i + 1];
                }
                else if (string.Equals(a, "-perfVd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out var v))
                {
                    vd = Mathf.Clamp(v, 1, 8);
                }
                else if (string.Equals(a, "-perfOut", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                }
                else if (string.Equals(a, "-seed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && long.TryParse(args[i + 1], out var s))
                {
                    seed = s;
                }
                else if (string.Equals(a, "-perfIdle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && float.TryParse(args[i + 1], out var fi))
                {
                    idle = Mathf.Clamp(fi, 5f, 600f);
                }
                else if (string.Equals(a, "-perfWalk", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && float.TryParse(args[i + 1], out var fw))
                {
                    walk = Mathf.Clamp(fw, 5f, 600f);
                }
                else if (string.Equals(a, "-perfFeature", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    feature = args[i + 1];
                }
                else if (string.Equals(a, "-perfTerrain", StringComparison.OrdinalIgnoreCase))
                {
                    terrain = true;
                }
                else if (string.Equals(a, "-perfDense", StringComparison.OrdinalIgnoreCase))
                {
                    dense = true;
                }
            }

            if (!on)
            {
                return;
            }

            // Automated samples must keep advancing if the desktop focus changes. Ordinary
            // gameplay retains the project's background-pause setting when this flag is absent.
            Application.runInBackground = true;
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<PerfProbe>();
            p._seed = seed;
            p._outDir = outDir;
            p._idleSeconds = idle;
            p._walkSeconds = walk;
            p._presetOverride = preset;
            p._vdOverride = vd;
            p._featureSpec = feature;
            p._dense = dense;
            p._terrain = terrain;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _input = new JourneyInputSource();
            _inputAttached = InputMap.AttachPerformanceInput(_input);
            if (!_inputAttached)
            {
                Debug.LogError("[PerfProbe] Could not own exclusive performance input; no measurement started.");
                Quit(5);
                yield break;
            }
            _input.Publish(default);
            var shell = FindAnyObjectByType<AppShell>();
            if (shell == null)
            {
                Debug.LogError("[PerfProbe] No AppShell in the scene.");
                Quit(1);
                yield break;
            }

            yield return WaitForPhase(shell, ShellPhase.MainMenu, 30f);

            // Optional settings overrides for comparable runs. Snapshot the real settings file first and
            // restore it on exit — AppShell may persist settings mid-run, and the probe must never change
            // what the player actually configured.
            if (!string.IsNullOrEmpty(_presetOverride) || _vdOverride > 0)
            {
                BackupSettingsFile();
                if (!string.IsNullOrEmpty(_presetOverride)
                    && Enum.TryParse<QualityPreset>(_presetOverride, ignoreCase: true, out var qp))
                {
                    shell.Settings.Preset = qp;
                }

                if (_vdOverride > 0)
                {
                    shell.Settings.ViewDistanceChunks = _vdOverride;
                }

                shell.Settings.Apply();
            }

            // Dense scene (#361): Extreme creature abundance on a breathable, vegetated planet so a heavy
            // creature pack (incl. glowers) auto-spawns around the player. Baseline runs pass no options.
            WorldCreationOptions worldOptions = _dense
                ? new WorldCreationOptions { Creatures = 4 /* Extreme */, StartPlanetType = DensePlanet }
                : null;

            Debug.Log($"[PerfProbe] Starting world (seed {_seed}, preset {shell.Settings.Preset}, view distance {shell.Settings.ViewDistanceChunks}{(_dense ? ", dense scene" : "")}).");
            shell.StartSingleplayerWorld(WorldName, _seed, creativeUnlockAll: false, creativeAllShips: false, creativeKit: false, worldOptions: worldOptions);

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            _boot = boot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError("[PerfProbe] World did not start (bundled server missing?).");
                Quit(1);
                yield break;
            }

            var phases = new List<PhaseResult>();
            yield return WaitForChunkReadiness("spawn");
            if (!_lastReadinessPassed)
            {
                WriteResults(shell, phases);
                RestoreSettingsFile();
                Quit(3);
                yield break;
            }

            // Per-feature overrides run AFTER the preset is applied and the gameplay camera exists (so the SSAO
            // renderer / SMAA choice on ActiveCameraData is live), isolating one feature's cost for #374.
            _featureTag = ApplyFeatureOverrides();
            if (_featureTag != null)
            {
                Debug.Log($"[PerfProbe] Feature overrides applied: {_featureTag}");
            }

            if (_pomRequested != null && !_pomApplied)
            {
                _measurementFailure = "requested_parallax_override_not_applied";
                WriteResults(shell, phases);
                RestoreSettingsFile();
                Quit(6);
                yield break;
            }

            // Phase 1: idle — steady-state cost with the spawn area fully streamed.
            PhaseResult r = null;
            yield return Sample("idle", _idleSeconds, x => r = x);
            phases.Add(r);
            if (!r.fixedIdleVerified)
            {
                _measurementFailure = "idle_camera_or_scene_not_stable";
                Debug.LogWarning("[PerfProbe] Idle sample was not a fixed-camera, settled scene; feature comparison remains unverified.");
                WriteResults(shell, phases);
                RestoreSettingsFile();
                Quit(4);
                yield break;
            }

            // The historical forward-input phase can stop against the cabin wall. Terrain preparation
            // is opt-in and recorded; actual displacement below decides whether traversal occurred.
            bool prepared = !_terrain;
            if (_terrain)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                prepared = pc != null && !boot.SpaceViewActive
                    && pc.PlaceForCaptureNear(boot.ShipPosition ?? boot.PlayerPosition, pitch: 12f);
                if (prepared)
                {
                    yield return WaitForChunkReadiness("terrain_pose");
                    if (!_lastReadinessPassed)
                    {
                        WriteResults(shell, phases);
                        RestoreSettingsFile();
                        Quit(3);
                        yield break;
                    }
                    yield return WaitUntil(() => pc.IsCaptureGrounded && !boot.Aboard, 12f);
                    prepared = pc.IsCaptureGrounded && !boot.Aboard && !pc.IsHeadUnderwater();
                }
                _terrainPrepared = prepared;
                if (!prepared) Debug.LogWarning("[PerfProbe] No safe terrain start; traversal remains unverified.");
            }
            _probeMove = prepared ? new Vector2(0f, 1f) : Vector2.zero;
            yield return Sample(_terrain ? "terrain_forward_input" : "spawn_forward_input", _walkSeconds, x => r = x);
            _probeMove = Vector2.zero;
            phases.Add(r);

            // Phase 3 (optional): dense — force visual midnight and stand among the Extreme creature pack so the
            // glowing entities' point lights dominate the frame (#361). Walking first left a filled ring; a short
            // extra settle lets it top up before sampling.
            if (_dense)
            {
                boot.SetCaptureEnvironment(0f); // midnight — see the DensePlanet/caveat note on the field above
                yield return new WaitForSecondsRealtime(DenseSettle);
                yield return WaitForChunkReadiness("dense");
                if (!_lastReadinessPassed)
                {
                    WriteResults(shell, phases);
                    RestoreSettingsFile();
                    Quit(3);
                    yield break;
                }
                yield return Sample("dense", _idleSeconds, x => r = x);
                phases.Add(r);
            }

            if (_terrain && !phases[1].traversalVerified) _measurementFailure = "terrain_traversal_unverified";
            WriteResults(shell, phases);
            RestoreSettingsFile();
            Quit(_terrain && !phases[1].traversalVerified ? 2 : 0);
        }

        private static string SettingsPath => Path.Combine(Application.persistentDataPath, "client_settings.json");

        private void BackupSettingsFile()
        {
            try
            {
                _settingsExisted = File.Exists(SettingsPath);
                _settingsBackup = _settingsExisted ? File.ReadAllBytes(SettingsPath) : null;
                _didBackup = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PerfProbe] Could not snapshot settings: {ex.Message}");
            }
        }

        private void RestoreSettingsFile()
        {
            if (!_didBackup)
            {
                return; // no override ran — never touch the player's settings file
            }

            try
            {
                if (_settingsExisted && _settingsBackup != null)
                {
                    File.WriteAllBytes(SettingsPath, _settingsBackup);
                }
                else if (!_settingsExisted && File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath); // fresh install: leave no trace of the override
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PerfProbe] Could not restore settings: {ex.Message}");
            }
        }

        private void OnApplicationQuit()
        {
            ReleaseInput();
            RestoreSettingsFile(); // also runs if the process is interrupted
        }

        /// <summary>Applies the <c>-perfFeature</c> overrides on top of the active preset, mutating the same
        /// runtime knobs <see cref="ClientSettings.Apply"/> owns (URP asset + the gameplay camera's URP data +
        /// the ground-scatter gate). Returns a short sanitized tag of what actually changed (for the output
        /// filename), or null when nothing was overridden. The process exits after the run, so this leaves no
        /// lasting state — no restore needed.</summary>
        private string ApplyFeatureOverrides()
        {
            if (string.IsNullOrEmpty(_featureSpec))
            {
                return null;
            }

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cam = ClientSettings.ActiveCameraData;
            var tags = new List<string>();

            foreach (var raw in _featureSpec.Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var parts = token.Split('=');
                string key = parts[0].Trim().ToLowerInvariant();
                string val = parts.Length > 1 ? parts[1].Trim() : "off";

                switch (key)
                {
                    case "ssao":
                        // Force a specific SSAO tier by renderer index so full/half/off can be compared in ONE
                        // thermal session (the SSAO cost split for #374): 0 = full-res, 2 = half-res, 1 = off.
                        if (cam != null)
                        {
                            switch (val)
                            {
                                case "full": cam.SetRenderer(0); tags.Add("ssaoFull"); break;
                                case "half": cam.SetRenderer(2); tags.Add("ssaoHalf"); break;
                                default: cam.SetRenderer(1); tags.Add("ssaoOff"); break;
                            }
                        }
                        break;
                    case "pom":
                        _pomRequested = val.ToLowerInvariant();
                        bool validPom = _pomRequested == "on" || _pomRequested == "off";
                        _pomApplied = validPom && BlockParallaxController.TryOverrideForPerformance(
                            _input, _pomRequested == "on", out _pomAppliedScale);
                        if (_pomApplied) tags.Add(_pomRequested == "on" ? "pomOn" : "pomOff");
                        else Debug.LogWarning("[PerfProbe] Requested POM override was not applied to a parallax material.");
                        break;
                    case "depth":
                    case "depthopaque":
                        // Drop the depth prepass + opaque colour copy (and tell the shaders they're gone, so
                        // water/fog fall back cleanly instead of sampling unbound textures).
                        if (urp != null) { urp.supportsCameraDepthTexture = false; urp.supportsCameraOpaqueTexture = false; }
                        Shader.SetGlobalFloat("_Sc_ScreenFx", 0f);
                        tags.Add("depthOff");
                        break;
                    case "smaa":
                        if (cam != null) { cam.antialiasing = AntialiasingMode.None; tags.Add("smaaOff"); }
                        break;
                    case "scatter":
                        GroundScatter.Enabled = false;
                        tags.Add("scatterOff");
                        break;
                    case "shadowmap":
                        if (urp != null && int.TryParse(val, out var sm) && sm > 0) { urp.mainLightShadowmapResolution = sm; tags.Add($"sm{sm}"); }
                        break;
                    case "shadowdist":
                        if (urp != null && float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sd) && sd >= 0f) { urp.shadowDistance = sd; tags.Add($"sd{sd:0}"); }
                        break;
                    default:
                        Debug.LogWarning($"[PerfProbe] Unknown -perfFeature token '{token}' ignored.");
                        break;
                }
            }

            return tags.Count > 0 ? string.Join("-", tags) : null;
        }

        [Serializable]
        private sealed class ChunkWorkSnapshot
        {
            public int dirty, buildsInFlight, pendingUploads, bakesInFlight, pendingAssignments;
            public int total, meshFailures, loadedDataChunks, loadedChunkObjects;
        }

        [Serializable]
        private sealed class ReadinessSample
        {
            public float elapsedSeconds;
            public ChunkWorkSnapshot work;
            public float dispatchMs, uploadMs, colliderAssignmentMs;
            public int worldEpoch;
            public bool worldReady;
            public float secondsSinceChunkArrival;
            public bool cinematicCameraActive, prologueActive, menuOpen, worldPaused, cameraAvailable;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public float cameraFieldOfView;
        }

        [Serializable]
        private sealed class ReadinessResult
        {
            public string name;
            public bool passed;
            public string reason;
            public float deadlineSeconds, requiredQuietSeconds, elapsedSeconds, finalQuietSeconds;
            public ChunkWorkSnapshot start, end;
            public ReadinessSample[] samples;
            public int observedFrames, meshDispatches, meshUploads, colliderAssignments;
            public float dispatchTotalMs, uploadTotalMs, colliderAssignmentTotalMs;
            public float dispatchPeakMs, uploadPeakMs, colliderAssignmentPeakMs;
        }

        private ChunkWorkSnapshot ReadChunkWork()
        {
            // This runs on the main thread. Read each producer before its completion queue, matching
            // GameBootstrap's enqueue-before-decrement transfer; brief double counts are conservative.
            var work = new ChunkWorkSnapshot
            {
                dirty = _boot.DirtyChunkCount,
                buildsInFlight = _boot.MeshBuildsInFlight,
                pendingUploads = _boot.PendingMeshUploads,
                bakesInFlight = _boot.ColliderBakesInFlight,
                pendingAssignments = _boot.PendingColliderAssignments,
                meshFailures = _boot.ChunkMeshFailureCount,
                loadedDataChunks = _boot.World.Chunks.Count,
                loadedChunkObjects = _boot.LoadedChunkObjectCount,
            };
            work.total = work.dirty + work.buildsInFlight + work.pendingUploads + work.bakesInFlight + work.pendingAssignments;
            return work;
        }

        private static void AccumulatePeak(ChunkWorkSnapshot peak, ChunkWorkSnapshot work)
        {
            peak.dirty = Math.Max(peak.dirty, work.dirty);
            peak.buildsInFlight = Math.Max(peak.buildsInFlight, work.buildsInFlight);
            peak.pendingUploads = Math.Max(peak.pendingUploads, work.pendingUploads);
            peak.bakesInFlight = Math.Max(peak.bakesInFlight, work.bakesInFlight);
            peak.pendingAssignments = Math.Max(peak.pendingAssignments, work.pendingAssignments);
            peak.total = Math.Max(peak.total, work.total);
            peak.meshFailures = Math.Max(peak.meshFailures, work.meshFailures);
            peak.loadedDataChunks = Math.Max(peak.loadedDataChunks, work.loadedDataChunks);
            peak.loadedChunkObjects = Math.Max(peak.loadedChunkObjects, work.loadedChunkObjects);
        }

        private IEnumerator WaitForChunkReadiness(string name)
        {
            Debug.Log($"[PerfProbe] Waiting for '{name}' terrain queues and cinematic camera to settle (deadline {ChunkReadyTimeout}s).");
            float started = Time.realtimeSinceStartup, quietSince = -1f, nextRecord = 0f;
            int epoch = _boot.WorldEpoch;
            var player = FindAnyObjectByType<PlayerController>();
            var camera = player != null ? player.Camera : null;
            var samples = new List<ReadinessSample>();
            var result = new ReadinessResult
            {
                name = name, deadlineSeconds = ChunkReadyTimeout, requiredQuietSeconds = ChunkQuietSeconds,
                start = ReadChunkWork(),
            };
            _lastReadinessPassed = false;
            while (true)
            {
                float now = Time.realtimeSinceStartup;
                result.elapsedSeconds = now - started;
                var work = ReadChunkWork();
                result.observedFrames++;
                result.meshDispatches += _boot.LastMeshDispatchCount;
                result.meshUploads += _boot.LastMeshUploadCount;
                result.colliderAssignments += _boot.LastColliderAssignmentCount;
                result.dispatchTotalMs += _boot.LastMeshDispatchMs;
                result.uploadTotalMs += _boot.LastMeshUploadMs;
                result.colliderAssignmentTotalMs += _boot.LastColliderAssignmentMs;
                result.dispatchPeakMs = Mathf.Max(result.dispatchPeakMs, _boot.LastMeshDispatchMs);
                result.uploadPeakMs = Mathf.Max(result.uploadPeakMs, _boot.LastMeshUploadMs);
                result.colliderAssignmentPeakMs = Mathf.Max(result.colliderAssignmentPeakMs, _boot.LastColliderAssignmentMs);
                bool empty = _boot.WorldReady && work.loadedDataChunks > 0 && work.total == 0
                    && work.meshFailures == 0 && _boot.TimeSinceLastChunk >= 0.6f
                    && camera != null && !_boot.CinematicCameraActive && !_boot.VegaPrologueActive
                    && !_boot.MenuOpen && !_boot.WorldPaused;
                // Wait for normal gameplay to release the real camera. The probe never dismisses
                // speech or skips an intro: that would silently change the measured user experience.
                if (_boot.WorldEpoch != epoch) { quietSince = -1f; epoch = _boot.WorldEpoch; }
                if (!empty) quietSince = -1f;
                else if (quietSince < 0f) quietSince = now;
                result.finalQuietSeconds = quietSince < 0f ? 0f : now - quietSince;
                bool passed = result.finalQuietSeconds >= ChunkQuietSeconds;
                bool timedOut = result.elapsedSeconds >= ChunkReadyTimeout;
                if (result.elapsedSeconds >= nextRecord || passed || timedOut)
                {
                    samples.Add(new ReadinessSample
                    {
                        elapsedSeconds = result.elapsedSeconds, work = work,
                        dispatchMs = _boot.LastMeshDispatchMs, uploadMs = _boot.LastMeshUploadMs,
                        colliderAssignmentMs = _boot.LastColliderAssignmentMs,
                        worldEpoch = _boot.WorldEpoch, worldReady = _boot.WorldReady,
                        secondsSinceChunkArrival = _boot.TimeSinceLastChunk,
                        cinematicCameraActive = _boot.CinematicCameraActive, prologueActive = _boot.VegaPrologueActive,
                        menuOpen = _boot.MenuOpen, worldPaused = _boot.WorldPaused, cameraAvailable = camera != null,
                        cameraPosition = camera != null ? camera.transform.position : Vector3.zero,
                        cameraRotation = camera != null ? camera.transform.rotation : Quaternion.identity,
                        cameraFieldOfView = camera != null ? camera.fieldOfView : 0f,
                    });
                    nextRecord = result.elapsedSeconds + 1f;
                }
                if (passed || timedOut)
                {
                    result.passed = passed;
                    result.reason = passed ? "queues_empty_and_cinematic_released_for_quiet_interval" : "scene_readiness_deadline_exceeded";
                    if (!passed) _measurementFailure = result.reason;
                    result.end = work;
                    result.samples = samples.ToArray();
                    _readiness.Add(result);
                    _lastReadinessPassed = passed;
                    Debug.Log($"[PerfProbe] Readiness '{name}': {result.reason} after {result.elapsedSeconds:0.0}s; "
                        + $"dirty={work.dirty}, builds={work.buildsInFlight}, uploads={work.pendingUploads}, "
                        + $"bakes={work.bakesInFlight}, assignments={work.pendingAssignments}, failures={work.meshFailures}, "
                        + $"cinematic={_boot.CinematicCameraActive}, prologue={_boot.VegaPrologueActive}, menu={_boot.MenuOpen}, paused={_boot.WorldPaused}.");
                    yield break;
                }
                yield return null;
            }
        }

        [Serializable]
        private sealed class PhaseResult
        {
            public string name;
            public int frames;
            public float seconds;
            public float avgMs;
            public float p50Ms;
            public float p95Ms;
            public float p99Ms;
            public float maxMs;
            public int framesOver33Ms;
            public int framesOver50Ms;
            public int framesOver100Ms;
            public int gcGen0;
            public int gcGen1;
            public int gcGen2;
            public long managedMemDeltaBytes;
            public long managedMemEndBytes;
            public long unityAllocatedEndBytes;
            public float[] frameTimesMs;
            public Vector3 positionStart;
            public Vector3 positionEnd;
            public float horizontalDisplacementMeters;
            public float travelledMeters;
            public int framesAboard;
            public int framesInSpace;
            public int framesFocused;
            public int framesUnfocused;
            public int framesExclusiveInputOwned;
            public int framesCinematicCameraActive, framesPrologueActive, framesMenuOpen, framesWorldPaused;
            public bool cameraAvailable, fixedIdleVerified;
            public Vector3 cameraPositionStart, cameraPositionEnd;
            public Quaternion cameraRotationStart, cameraRotationEnd;
            public float cameraFovStart, cameraFovEnd;
            public float maxCameraDistanceFromStart, maxCameraAngleFromStart, maxCameraFovDeltaFromStart;
            public Vector3[] cameraPositions;
            public Quaternion[] cameraRotations;
            public float[] cameraFieldsOfView;
            public int terrainChunksVisited;
            public int peakMeshBacklog;
            public ChunkWorkSnapshot chunkWorkStart, chunkWorkEnd, chunkWorkPeak;
            public float[] meshDispatchTimesMs, meshUploadTimesMs, colliderAssignmentTimesMs;
            public int[] pendingChunkWork;
            public int meshDispatches, meshUploads, colliderAssignments;
            public bool traversalVerified;
            // -1 means the player/platform did not expose the counter, not zero rendering work.
            public long peakDrawCalls = -1;
            public long peakSetPassCalls = -1;
            public long peakVertices = -1;
        }

        [Serializable]
        private sealed class ProbeResult
        {
            public string capturedUtc;
            public string appVersion;
            public string unity;
            public string platform;
            public string qualityPreset;
            public int viewDistanceChunks;
            public long seed;
            public string device;
            public string graphicsApi;
            public int width;
            public int height;
            public int vSyncCount;
            public int targetFrameRate;
            public float renderScale;
            public bool runInBackground;
            public string inputPolicy;
            public bool terrainProbeRequested;
            public bool terrainPosePrepared;
            public string featureRequested;
            public string parallaxOverrideRequested;
            public bool parallaxOverrideApplied;
            public float parallaxAppliedScale;
            public string featureOverride; // null unless -perfFeature changed something (the itemization run)
            public PhaseResult[] phases;
            public ReadinessResult[] readiness;
            public bool measurementValid;
            public string measurementFailure;
            public float fixedCameraPositionToleranceMeters, fixedCameraAngleToleranceDegrees, fixedCameraFovToleranceDegrees;
        }

        private IEnumerator Sample(string name, float seconds, Action<PhaseResult> done)
        {
            Debug.Log($"[PerfProbe] Phase '{name}' — sampling {seconds:0}s...");
            var samples = new List<float>(Mathf.CeilToInt(seconds) * 300); // generous: fits 300 FPS without regrowth
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            long mem = GC.GetTotalMemory(false);

            var player = FindAnyObjectByType<PlayerController>();
            var camera = player != null ? player.Camera : null;
            bool cameraAvailable = camera != null;
            Vector3 cameraStart = cameraAvailable ? camera.transform.position : Vector3.zero;
            Quaternion cameraRotationStart = cameraAvailable ? camera.transform.rotation : Quaternion.identity;
            float cameraFovStart = cameraAvailable ? camera.fieldOfView : 0f;
            var cameraPositions = new List<Vector3>();
            var cameraRotations = new List<Quaternion>();
            var cameraFovs = new List<float>();
            float cameraDistance = 0f, cameraAngle = 0f, cameraFovDelta = 0f;
            int cinematicFrames = 0, prologueFrames = 0, menuFrames = 0, pausedFrames = 0;
            Vector3 start = _boot.PlayerPosition, previous = start;
            float travelled = 0f;
            var workStart = ReadChunkWork();
            var workPeak = new ChunkWorkSnapshot();
            var dispatchTimes = new List<float>();
            var uploadTimes = new List<float>();
            var colliderTimes = new List<float>();
            var workCounts = new List<int>();
            int dispatches = 0, uploads = 0, assignments = 0;
            int aboardFrames = 0, spaceFrames = 0, focusedFrames = 0, unfocusedFrames = 0, backlog = 0;
            int exclusiveInputFrames = 0;
            long drawCalls = -1, setPassCalls = -1, vertices = -1;
            var chunks = new HashSet<Vector2Int>();
            float t = 0f;
            yield return null; // don't count the setup frame
            while (t < seconds)
            {
                float dt = Time.unscaledDeltaTime;
                samples.Add(dt * 1000f);
                Vector3 position = _boot.PlayerPosition;
                travelled += WrappedDifference(position, previous).magnitude;
                previous = position;
                if (_boot.Aboard) aboardFrames++;
                if (_boot.InSpace) spaceFrames++;
                if (_boot.CinematicCameraActive) cinematicFrames++;
                if (_boot.VegaPrologueActive) prologueFrames++;
                if (_boot.MenuOpen) menuFrames++;
                if (_boot.WorldPaused) pausedFrames++;
                if (camera == null) cameraAvailable = false;
                Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
                Quaternion cameraRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
                float cameraFov = camera != null ? camera.fieldOfView : 0f;
                cameraPositions.Add(cameraPosition);
                cameraRotations.Add(cameraRotation);
                cameraFovs.Add(cameraFov);
                cameraDistance = Mathf.Max(cameraDistance, Vector3.Distance(cameraStart, cameraPosition));
                cameraAngle = Mathf.Max(cameraAngle, Quaternion.Angle(cameraRotationStart, cameraRotation));
                cameraFovDelta = Mathf.Max(cameraFovDelta, Mathf.Abs(cameraFovStart - cameraFov));
                if (InputMap.OwnsVerificationInput(_input)) exclusiveInputFrames++;
                if (Application.isFocused) focusedFrames++;
                else unfocusedFrames++;
                if (!_boot.Aboard && !_boot.InSpace)
                    chunks.Add(new Vector2Int(WorldConstants.WorldToChunk(Mathf.FloorToInt(position.x)),
                        WorldConstants.WorldToChunk(Mathf.FloorToInt(position.z))));
                var work = ReadChunkWork();
                backlog = Mathf.Max(backlog, work.total);
                AccumulatePeak(workPeak, work);
                workCounts.Add(work.total);
                dispatchTimes.Add(_boot.LastMeshDispatchMs);
                uploadTimes.Add(_boot.LastMeshUploadMs);
                colliderTimes.Add(_boot.LastColliderAssignmentMs);
                dispatches += _boot.LastMeshDispatchCount;
                uploads += _boot.LastMeshUploadCount;
                assignments += _boot.LastColliderAssignmentCount;
                if (_drawCalls.Valid) drawCalls = Math.Max(drawCalls, _drawCalls.LastValue);
                if (_setPassCalls.Valid) setPassCalls = Math.Max(setPassCalls, _setPassCalls.LastValue);
                if (_vertices.Valid) vertices = Math.Max(vertices, _vertices.LastValue);
                t += dt;
                yield return null;
            }

            var r = new PhaseResult
            {
                name = name,
                frames = samples.Count,
                seconds = t,
                gcGen0 = GC.CollectionCount(0) - gc0,
                gcGen1 = GC.CollectionCount(1) - gc1,
                gcGen2 = GC.CollectionCount(2) - gc2,
                managedMemDeltaBytes = GC.GetTotalMemory(false) - mem,
                managedMemEndBytes = GC.GetTotalMemory(false),
                unityAllocatedEndBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                frameTimesMs = samples.ToArray(),
                positionStart = start,
                positionEnd = _boot.PlayerPosition,
                horizontalDisplacementMeters = new Vector2(WrappedDifference(_boot.PlayerPosition, start).x,
                    WrappedDifference(_boot.PlayerPosition, start).z).magnitude,
                travelledMeters = travelled,
                framesAboard = aboardFrames,
                framesInSpace = spaceFrames,
                framesFocused = focusedFrames,
                framesUnfocused = unfocusedFrames,
                framesExclusiveInputOwned = exclusiveInputFrames,
                framesCinematicCameraActive = cinematicFrames,
                framesPrologueActive = prologueFrames,
                framesMenuOpen = menuFrames,
                framesWorldPaused = pausedFrames,
                cameraAvailable = cameraAvailable,
                cameraPositionStart = cameraStart,
                cameraPositionEnd = camera != null ? camera.transform.position : Vector3.zero,
                cameraRotationStart = cameraRotationStart,
                cameraRotationEnd = camera != null ? camera.transform.rotation : Quaternion.identity,
                cameraFovStart = cameraFovStart,
                cameraFovEnd = camera != null ? camera.fieldOfView : 0f,
                cameraPositions = cameraPositions.ToArray(),
                cameraRotations = cameraRotations.ToArray(),
                cameraFieldsOfView = cameraFovs.ToArray(),
                maxCameraDistanceFromStart = cameraDistance,
                maxCameraAngleFromStart = cameraAngle,
                maxCameraFovDeltaFromStart = cameraFovDelta,
                terrainChunksVisited = chunks.Count,
                peakMeshBacklog = backlog,
                chunkWorkStart = workStart,
                chunkWorkEnd = ReadChunkWork(),
                chunkWorkPeak = workPeak,
                meshDispatchTimesMs = dispatchTimes.ToArray(),
                meshUploadTimesMs = uploadTimes.ToArray(),
                colliderAssignmentTimesMs = colliderTimes.ToArray(),
                pendingChunkWork = workCounts.ToArray(),
                meshDispatches = dispatches,
                meshUploads = uploads,
                colliderAssignments = assignments,
                peakDrawCalls = drawCalls,
                peakSetPassCalls = setPassCalls,
                peakVertices = vertices,
            };

            r.fixedIdleVerified = name == "idle" && r.frames > 0 && cameraAvailable
                && exclusiveInputFrames == r.frames
                && cinematicFrames == 0 && prologueFrames == 0 && menuFrames == 0 && pausedFrames == 0
                && workStart.total == 0 && r.chunkWorkEnd.total == 0 && workPeak.total == 0 && workPeak.meshFailures == 0
                && cameraDistance <= FixedCameraPositionTolerance && cameraAngle <= FixedCameraAngleTolerance
                && cameraFovDelta <= FixedCameraFovTolerance;
            r.traversalVerified = name == "terrain_forward_input" && r.frames > 0
                && aboardFrames == 0 && spaceFrames == 0 && chunks.Count >= 3
                && r.horizontalDisplacementMeters >= WorldConstants.ChunkSize * 2;
            float sum = 0f, max = 0f;
            foreach (float ms in samples)
            {
                sum += ms;
                if (ms > max) max = ms;
                if (ms > HitchMs33) r.framesOver33Ms++;
                if (ms > 50f) r.framesOver50Ms++;
                if (ms > HitchMs100) r.framesOver100Ms++;
            }

            samples.Sort();
            r.avgMs = samples.Count > 0 ? sum / samples.Count : 0f;
            r.p50Ms = Percentile(samples, 0.50f);
            r.p95Ms = Percentile(samples, 0.95f);
            r.p99Ms = Percentile(samples, 0.99f);
            r.maxMs = max;
            done(r);
        }

        private Vector3 WrappedDifference(Vector3 a, Vector3 b) => new Vector3(
            (float)WorldConstants.WrapDeltaX(a.x - b.x, _boot.Circumference), a.y - b.y,
            (float)WorldConstants.WrapDeltaZ(a.z - b.z, _boot.Circumference));

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[i];
        }

        private void WriteResults(AppShell shell, List<PhaseResult> phases)
        {
            var result = new ProbeResult
            {
                capturedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                appVersion = Application.version,
                unity = Application.unityVersion,
                platform = Application.platform.ToString(),
                qualityPreset = shell.Settings.Preset.ToString(),
                viewDistanceChunks = shell.Settings.ViewDistanceChunks,
                seed = _seed,
                device = $"{SystemInfo.processorType} / {SystemInfo.graphicsDeviceName} / {SystemInfo.systemMemorySize} MB",
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                width = Screen.width,
                height = Screen.height,
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                renderScale = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.renderScale : 1f,
                runInBackground = Application.runInBackground,
                inputPolicy = "exclusive_perf_source; native InputMap backends and legacy ScriptedMove excluded",
                featureRequested = _featureSpec,
                parallaxOverrideRequested = _pomRequested,
                parallaxOverrideApplied = _pomApplied,
                parallaxAppliedScale = _pomAppliedScale,
                featureOverride = _featureTag,
                terrainProbeRequested = _terrain,
                terrainPosePrepared = _terrainPrepared,
                phases = phases.ToArray(),
                readiness = _readiness.ToArray(),
                measurementValid = phases.Count > 0 && phases[0].fixedIdleVerified && _readiness.TrueForAll(x => x.passed)
                    && (!_terrain || phases.Count > 1 && phases[1].traversalVerified),
                measurementFailure = _measurementFailure,
                fixedCameraPositionToleranceMeters = FixedCameraPositionTolerance,
                fixedCameraAngleToleranceDegrees = FixedCameraAngleTolerance,
                fixedCameraFovToleranceDegrees = FixedCameraFovTolerance,
            };

            string dir = !string.IsNullOrEmpty(_outDir) ? _outDir : Path.Combine(Application.persistentDataPath, "perf");
            Directory.CreateDirectory(dir);
            string featureSuffix = string.IsNullOrEmpty(_featureTag) ? "" : $"_{_featureTag}";
            string denseSuffix = (_dense ? "_dense" : "") + (_terrain ? "_terrain" : "");
            string baseName = $"perf_baseline_{Application.platform}_{result.qualityPreset}_vd{result.viewDistanceChunks}{denseSuffix}{featureSuffix}";
            string jsonPath = Path.Combine(dir, baseName + ".json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(result, prettyPrint: true));

            var txt = new StringBuilder();
            txt.AppendLine($"Perf baseline — {result.capturedUtc} UTC — v{result.appVersion} — {result.platform}");
            txt.AppendLine($"Preset {result.qualityPreset}, view distance {result.viewDistanceChunks}, seed {result.seed}");
            if (!string.IsNullOrEmpty(result.featureOverride))
            {
                txt.AppendLine($"Feature override: {result.featureOverride}");
            }
            txt.AppendLine(result.device);
            txt.AppendLine($"{result.width}×{result.height}, {result.graphicsApi}, render scale {result.renderScale:0.00}, "
                         + $"vSync {result.vSyncCount}, target FPS {result.targetFrameRate}, background execution {result.runInBackground}");
            txt.AppendLine($"Measurement valid: {result.measurementValid}; failure: {result.measurementFailure ?? "none"}.");
            foreach (var ready in result.readiness)
                txt.AppendLine($"Readiness [{ready.name}]: {ready.reason}, {ready.elapsedSeconds:0.0}s / "
                    + $"{ready.deadlineSeconds:0}s deadline, quiet {ready.finalQuietSeconds:0.0}s, end backlog {ready.end.total}.");
            foreach (var ph in result.phases)
            {
                txt.AppendLine($"[{ph.name}] {ph.frames} frames / {ph.seconds:0.0}s — avg {ph.avgMs:0.00} ms ({1000f / Mathf.Max(0.001f, ph.avgMs):0} FPS), "
                             + $"p50 {ph.p50Ms:0.00}, p95 {ph.p95Ms:0.00}, p99 {ph.p99Ms:0.00}, max {ph.maxMs:0.0} ms; "
                             + $">33ms: {ph.framesOver33Ms}, >50ms: {ph.framesOver50Ms}, >100ms: {ph.framesOver100Ms}; "
                             + $"GC {ph.gcGen0}/{ph.gcGen1}/{ph.gcGen2}, managed Δ {ph.managedMemDeltaBytes / (1024f * 1024f):0.0} MB");
                txt.AppendLine($"  displacement {ph.horizontalDisplacementMeters:0.0}m, path {ph.travelledMeters:0.0}m, "
                    + $"terrain chunks {ph.terrainChunksVisited}, aboard frames {ph.framesAboard}, space frames {ph.framesInSpace}, "
                    + $"peak mesh backlog {ph.peakMeshBacklog}, traversal verified {ph.traversalVerified}");
                txt.AppendLine($"  focused frames {ph.framesFocused}, unfocused frames {ph.framesUnfocused}");
                txt.AppendLine($"  fixed idle verified {ph.fixedIdleVerified}; camera max offset {ph.maxCameraDistanceFromStart:0.000}m, "
                    + $"angle {ph.maxCameraAngleFromStart:0.000}°, FOV delta {ph.maxCameraFovDeltaFromStart:0.000}°; "
                    + $"cinematic/prologue/menu/paused frames {ph.framesCinematicCameraActive}/{ph.framesPrologueActive}/{ph.framesMenuOpen}/{ph.framesWorldPaused}");
                txt.AppendLine($"  chunk work start/end/peak {ph.chunkWorkStart.total}/{ph.chunkWorkEnd.total}/{ph.chunkWorkPeak.total}; "
                    + $"dispatches/uploads/collider completions {ph.meshDispatches}/{ph.meshUploads}/{ph.colliderAssignments}");
            }

            string txtPath = Path.Combine(dir, baseName + ".txt");
            File.WriteAllText(txtPath, txt.ToString());
            Debug.Log($"[PerfProbe] Results written to {jsonPath}\n{txt}");
        }

        private static IEnumerator WaitForPhase(AppShell shell, ShellPhase phase, float timeout)
        {
            float t = 0f;
            while (shell.Phase != phase && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void Quit(int code)
        {
            ReleaseInput();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
