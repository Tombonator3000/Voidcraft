// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Automated marketing-screenshot capture. When the game is launched with the <c>-captureShots</c>
    /// command-line flag (a built player run, the canonical path) — or triggered from the editor
    /// "BlocksBeyondTheStars → Capture Screenshots" menu — this self-installs, drives the real client
    /// through a fixed sequence of scenes and writes a PNG of each to <c>marketing/screenshots/&lt;lang&gt;/</c>:
    /// <list type="number">
    ///   <item>start_screen — the main menu</item>
    ///   <item>planet_surface — the player on foot on the home world</item>
    ///   <item>cockpit_hud — the ship cockpit HUD</item>
    ///   <item>space_flight_1..3 — three in-flight vantage points in space (HUD + ship + a planet behind)</item>
    /// </list>
    /// ONE language per run (set with <c>-lang de|en</c>): the in-game HUD language is fixed when the world
    /// starts (WorldRig sets <c>boot.Locale</c> from <c>Settings.Language</c>), so a clean DE/EN set is two runs.
    ///
    /// Flight uses ordinary gameplay intents; outdoor comparisons use scripted player poses and visual
    /// time overrides. These are rendered visual evidence, not proof of a new-player gameplay journey. Capture reuses the proven full-frame recipe
    /// (<see cref="ScreenCapture.CaptureScreenshotAsTexture"/>, which includes the ScreenSpaceOverlay HUD,
    /// like the /bump screenshot). The timings and the three flight framings are the parts most likely to
    /// need tuning on a real run — adjust the constants below.
    /// </summary>
    public sealed class ScreenshotDirector : MonoBehaviour
    {
        // --- tunables ---
        private const string WorldName = "MarketingShots";
        private const long DefaultSeed = 424242L;     // a fixed, reproducible world; override with -seed
        private const int ShotWidth = 1920;            // web resolution (Full HD)
        private const int ShotHeight = 1080;
        private const float MenuSettle = 2.0f;         // after the menu appears, before the start_screen shot
        private const float WorldLoadTimeout = 90f;    // give up waiting for the world to load
        private const float ChunkSettle = 5.0f;        // after WorldReady, let chunks mesh / the veil fully lower
        private const float PoseSettle = 2.5f;         // after a pose change, before the shot
        private const float FlightHeading = 0f; // flight heading that frames the asteroids/planet behind the ship
        private const float CaptureReadyTimeout = 14f; // give the player this long to settle on solid, dry ground

        private string _lang = "en";
        private long _seed = DefaultSeed;
        private string _outDir;
        private string _worldName = WorldName;
        private bool _headless; // true = launched via the -captureShots command line (exit the process when done)
        private string _planet; // when set (-planet <key>), capture ONLY that planet's surface (surface_<key>.png)
        private string _startPlanet; // optional -startPlanet selects the world for the full concept sequence
        private bool _concepts; // additional actual-player material/site views, with scripted pose setup
        private bool _credits;  // when true (-captureCredits), capture ONLY the credits screen (credits.png) and quit
        private JourneyInputSource _input;
        private int _failedViews;
        private bool _quitting;

        /// <summary>Self-install at startup when capture is requested. Reload-safe: reads config fresh from the
        /// command line (player/headless) or EditorPrefs (editor menu) rather than relying on static state.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (!CaptureRequested(out string lang, out string outDir, out long seed, out bool headless, out string planet, out bool credits))
            {
                return;
            }

            // Command-line captures must progress even when another desktop window has focus.
            // Keep ordinary gameplay and the editor menu's capture setting unchanged.
            if (headless) Application.runInBackground = true;
            var go = new GameObject("ScreenshotDirector");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<ScreenshotDirector>();
            d._lang = lang;
            d._outDir = outDir;
            d._seed = seed;
            d._headless = headless;
            d._planet = planet;
            d._credits = credits;
            d._concepts = Array.Exists(Environment.GetCommandLineArgs(),
                a => string.Equals(a, "-captureConcepts", StringComparison.OrdinalIgnoreCase));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], "-startPlanet", StringComparison.OrdinalIgnoreCase)) d._startPlanet = args[i + 1];
                else if (string.Equals(args[i], "-captureWorld", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(args[i + 1])) d._worldName = args[i + 1];
            }
        }

        private static bool CaptureRequested(out string lang, out string outDir, out long seed, out bool headless, out string planet, out bool credits)
        {
            lang = "en";
            outDir = null;
            seed = DefaultSeed;
            headless = false;
            planet = null;
            credits = false;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-captureShots", StringComparison.OrdinalIgnoreCase))
                {
                    headless = true;
                }
                else if (string.Equals(a, "-captureCredits", StringComparison.OrdinalIgnoreCase))
                {
                    headless = true;
                    credits = true;
                }
                else if (string.Equals(a, "-lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    lang = args[i + 1];
                }
                else if (string.Equals(a, "-shotOut", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                }
                else if (string.Equals(a, "-seed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                         && long.TryParse(args[i + 1], out var s))
                {
                    seed = s;
                }
                else if (string.Equals(a, "-planet", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    planet = args[i + 1];
                }
            }

            bool on = headless;
#if UNITY_EDITOR
            // Editor menu-item runs (no command-line flag): fall back to a one-shot EditorPrefs trigger.
            if (!on && UnityEditor.EditorPrefs.GetBool("bbs_capture", false))
            {
                on = true;
                lang = UnityEditor.EditorPrefs.GetString("bbs_capture_lang", "en");
                UnityEditor.EditorPrefs.SetBool("bbs_capture", false); // consume it
            }
#endif
            lang = lang == "de" ? "de" : "en";
            return on;
        }

        private void Start() => StartCoroutine(Run());

        private void Update()
        {
            if (_input == null || _quitting) return;
            if (!InputMap.OwnsVerificationInput(_input))
            {
                Debug.LogError("[Capture] Exclusive gameplay input ownership was lost.");
                StopAllCoroutines();
                Quit(3);
                return;
            }
            _input.Publish(default);
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
            {
                Debug.LogWarning("[Capture] Canceled through native Escape/controller cancel.");
                StopAllCoroutines();
                Quit(3);
            }
        }

        private void OnDisable()
        {
            _input?.Clear();
            if (_input != null) InputMap.DetachVerificationInput(_input);
        }

        private IEnumerator Run()
        {
            _input = new JourneyInputSource();
            if (!InputMap.AttachCaptureInput(_input))
            {
                Debug.LogError("[Capture] Could not acquire exclusive gameplay input.");
                Quit(3);
                yield break;
            }
            _input.Publish(default);
            Screen.SetResolution(ShotWidth, ShotHeight, FullScreenMode.FullScreenWindow);

            var shell = FindAnyObjectByType<AppShell>();
            if (shell == null)
            {
                Debug.LogError("[Capture] No AppShell in the scene.");
                Quit(1);
                yield break;
            }

            // Force the capture language before anything localized is built (menu + HUD both follow this).
            shell.Settings.Language = _lang;
            shell.LoadLocalizer();

            string dir = ResolveOutDir();
            Directory.CreateDirectory(dir);
            Debug.Log($"[Capture] lang={_lang} seed={_seed} planet={_planet ?? "(none)"} credits={_credits} out={dir} "
                + $"runInBackground={Application.runInBackground} focused={Application.isFocused}");

            // Credits-only mode (-captureCredits): from the main menu, open the credits screen, let it build,
            // capture it, and quit. Used to verify the credits layout without a full marketing run.
            if (_credits)
            {
                yield return WaitForPhase(shell, ShellPhase.MainMenu, 30f);
                yield return new WaitForSecondsRealtime(MenuSettle);
                Debug.Log($"[Capture] phase before GoTo = {shell.Phase}");
                // Force credits and HOLD it there for a few seconds — defeats any late splash/menu transition
                // that would otherwise bounce the phase back to MainMenu before the shot.
                float held = 0f;
                while (held < 3f)
                {
                    if (shell.Phase != ShellPhase.Credits)
                    {
                        shell.GoTo(ShellPhase.Credits);
                    }

                    held += Time.unscaledDeltaTime;
                    yield return null;
                }

                Debug.Log($"[Capture] phase at shot = {shell.Phase}");
                yield return Capture(Path.Combine(dir, "credits.png"));
                Debug.Log("[Capture] Done (credits).");
                Quit(0);
                yield break;
            }

            // Planet-variety mode: when -planet <key> is given, shoot ONLY that planet's surface and quit.
            // The menu/cockpit/flight shots are planet-agnostic, so they're skipped — the ps1 loops this over a
            // curated list of planet types to build a "many worlds" gallery.
            if (!string.IsNullOrEmpty(_planet))
            {
                yield return CapturePlanetSurface(shell, dir);
                Debug.Log("[Capture] Done (planet surface).");
                Quit(0);
                yield break;
            }

            // 1) Start screen — wait for the studio/title splash chain to auto-advance to the main menu.
            yield return WaitForPhase(shell, ShellPhase.MainMenu, 30f);
            yield return new WaitForSecondsRealtime(MenuSettle);
            yield return Capture(Path.Combine(dir, "start_screen.png"));

            // 2) Start a fixed singleplayer world. Needs the bundled local server in StreamingAssets
            //    (publish-local-server.ps1 / a full build); otherwise the world never connects.
            // Sandbox worlds keep enemies, bandit turrets and the temperature hazard off (they all gate on
            // Survival), so no "Taking damage!" warning or attack fx can land in a frame — the HUD itself
            // (bars, minimap, hotbar) looks the same as in Survival.
            shell.StartSingleplayerWorld(_worldName, _seed, creativeUnlockAll: true, creativeAllShips: true, creativeKit: true,
                sandbox: true, worldOptions: string.IsNullOrEmpty(_startPlanet) ? null
                    : new WorldCreationOptions { StartPlanetType = _startPlanet });

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError("[Capture] World did not start (bundled server missing?). Aborting.");
                Quit(1);
                yield break;
            }

            // Wait until the world has FULLY revealed — the loading curtain has faded — not merely WorldReady, which
            // defaults true and can pass this instant before the load flips it false, catching the cockpit shot on
            // the black "Loading world" veil. Gating on the veil implicitly waits for WorldReady (the veil needs it
            // to fade), and is robust to that default-true race.
            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            var overlay = FindAnyObjectByType<WorldLoadingOverlay>();
            yield return WaitUntil(() => overlay == null || !overlay.VeilActive, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // 3) Cockpit HUD — a fresh world starts the player INSIDE the ship (the onboarding cockpit), so just
            //    capture the spawn view. No intent: ExitShip/EnterShip are space-only here and only pop a toast.
            yield return Capture(Path.Combine(dir, "cockpit_hud.png"));

            // 3b) In-game menu (the Tab menu) over the cockpit — open it exactly as Tab does, capture, close again
            //     so the following shots aren't covered by the menu. (The OS cursor isn't in a ScreenCapture RT.)
            var menu = FindAnyObjectByType<GameMenu>();
            if (menu != null)
            {
                menu.SetMenuOpen(true);
                yield return new WaitForSecondsRealtime(PoseSettle);
                Cursor.visible = false;
                yield return Capture(Path.Combine(dir, "cockpit_menu.png"));
                menu.SetMenuOpen(false);
            }

            if (_concepts) yield return CaptureHomeViews(boot, dir);

            // 4) Space flight — take off while still cleanly ABOARD (right after spawn, BEFORE stepping outside).
            //    Stepping out of the hull clears the server's aboard state, after which EnterSpace is refused and
            //    you'd stay on the planet — so flight must come first. One shot. SpaceView re-sends ShipMove at
            //    12 Hz from its own _yaw, so we set _yaw (SetFlightYaw) to choose the heading.
            boot.Network.SendEnterSpace();
            yield return WaitUntil(() => boot.InSpace, 25f);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // Aim at the nearest landable body (normally the home planet below) so the shot shows ship +
            // planet instead of empty starfield; the fixed heading is only the no-bodies fallback.
            var space = FindAnyObjectByType<SpaceView>();
            if (space != null && !space.CaptureAimAtNearestBody())
            {
                space.SetFlightYaw(FlightHeading);
            }

            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(dir, "space_flight.png"));

            // 5) Planet surface — land back on the home world, then step the on-foot player OUT of the ship onto
            //    open terrain WELL AWAY from the hull. No-arg LeaveSpace lands but keeps you inside the hull in the
            //    PLANET world, so the capture-pose step works (no input → the on-foot player can't move otherwise).
            boot.Network.SendLeaveSpace();
            yield return WaitUntil(() => !boot.InSpace, 25f);
            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // Terrain-aware placement (a good distance back from the ship, on solid/open/dry ground) and shoot only
            // once the player has actually settled on the surface — so the landed ship reads as a background element
            // instead of the hull/door filling the frame. Falls back to a blind offset if no near chunk collider is
            // ready in time, so we still write a frame. See PlayerController.PlaceForCaptureNear.
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                var p = boot.PlayerPosition;
                var anchor = new Vector3(p.x, p.y, p.z);
                bool placed = false;
                float t = 0f;
                while (t < CaptureReadyTimeout)
                {
                    if (!placed)
                    {
                        placed = pc.PlaceForCaptureNear(anchor, pitch: 18f);
                    }

                    bool alive = !boot.AwaitingRespawnConfirm && boot.Health > 0f;
                    if (placed && pc.IsCaptureGrounded && !pc.IsHeadUnderwater() && alive)
                    {
                        break;
                    }

                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!placed)
                {
                    // Blind fallback: step out diagonally and look AWAY from the ship (yaw 45° = toward +x/+z, the
                    // same corner we stepped to) so we still write a frame if no chunk collider was ready in time.
                    Debug.LogWarning("[Capture] planet_surface: terrain-aware placement failed — using blind fallback pose.");
                    pc.SetCapturePose(new Vector3(p.x + 16f, p.y + 1f, p.z + 16f), 45f, 4f);
                }
            }

            // Pin clear-weather noon so the on-foot shot is bright regardless of the home world's time/weather. The
            // player is already placed, so its longitude offset is final.
            boot.SetCaptureEnvironment(0.5f);

            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(dir, "planet_surface.png"));

            if (_concepts) yield return CaptureConceptViews(boot, dir);
            Debug.Log("[Capture] Done.");
            Quit(0);
        }

        /// <summary>Actual rendered concept comparisons. Camera placement is scripted and does not
        /// demonstrate player navigation or quest completion. Missing safe views are logged, not fabricated.</summary>
        private IEnumerator CaptureHomeViews(GameBootstrap boot, string dir)
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc == null || pc.Camera == null) yield break;
            yield return WaitUntil(() => !boot.CinematicCameraActive && !boot.VegaPrologueActive, 120f);
            if (boot.CinematicCameraActive || boot.VegaPrologueActive)
            {
                Debug.LogWarning("[Capture] Home views unavailable: cinematic still owns the camera.");
                _failedViews++;
                yield break;
            }
            LandedShipModel home = null;
            foreach (var ship in boot.LandedShips.Values)
                if (ship.OwnerId == boot.LocalPlayerId) { home = ship; break; }
            if (home == null) yield break;
            Vector3 saved = pc.transform.position;
            float savedYaw = pc.transform.eulerAngles.y;
            float savedPitch = Mathf.DeltaAngle(0f, pc.Camera.transform.localEulerAngles.x);
            for (int view = 0; view < 2; view++)
            {
                bool grounded = false;
                // A single rear aisle pose repeatedly failed the physical grounding check. Try a small
                // observed aisle range; an image still requires a real grounded capsule and quiet world.
                for (int candidate = 0; candidate < 3 && !grounded; candidate++)
                {
                    int x = home.Width / 2;
                    int z = view == 0 ? 1 + candidate : home.Length - 3 - candidate;
                    var floor = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(x, 0, z);
                    if (z <= 0 || z >= home.Length - 1 || home.Get(floor).IsAir
                        || !home.Get(floor + new BlocksBeyondTheStars.Shared.Geometry.Vector3i(0, 1, 0)).IsAir
                        || !home.Get(floor + new BlocksBeyondTheStars.Shared.Geometry.Vector3i(0, 2, 0)).IsAir)
                        continue;
                    Vector3 feet = boot.ScenePos(home.Origin.X + x + 0.5f, home.Origin.Y + 1.03f,
                        home.Origin.Z + z + 0.5f);
                    pc.SetCapturePose(feet, view == 0 ? 0f : 180f, 4f);
                    yield return WaitUntil(() => pc.IsCaptureGrounded, 10f);
                    grounded = pc.IsCaptureGrounded;
                    Debug.Log($"[Capture] Home view {view} candidate={candidate} grounded={grounded} "
                        + $"actual={pc.transform.position} requested={feet} menu={boot.MenuOpen} "
                        + $"paused={boot.WorldPaused} cinematic={boot.CinematicCameraActive} flying={pc.Flying} "
                        + $"timeScale={Time.timeScale} deltaTime={Time.deltaTime}");
                }
                if (!grounded)
                {
                    Debug.LogWarning($"[Capture] Home view {view} unavailable: no grounded capsule in the observed aisle candidates.");
                    _failedViews++;
                    continue;
                }
                yield return new WaitForSecondsRealtime(PoseSettle);
                yield return Capture(Path.Combine(dir, view == 0 ? "ship_home_forward.png" : "ship_home_aft.png"), cleanWorld: true);
            }
            pc.SetCapturePose(saved, savedYaw, savedPitch);
            yield return WaitUntil(() => pc.IsCaptureGrounded, 10f);
        }

        private IEnumerator CaptureConceptViews(GameBootstrap boot, string dir)
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc == null || pc.Camera == null) yield break;
            // Concept comparisons use the approved warm-amber / cool-indigo lighting target. This is capture-only;
            // ordinary gameplay retains the authoritative world clock and weather.
            boot.SetCaptureEnvironment(0.28f);
            pc.SetLookAngles(pc.transform.eulerAngles.y, 8f);
            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(dir, "terrain_materials.png"), cleanWorld: true);

            var site = Array.Find(boot.PlanetPois, p => p.Type == "veyl_signal");
            if (site == null)
            {
                Debug.LogWarning("[Capture] Veyl view unavailable: no uncompleted site on this world.");
                _failedViews++;
                yield break;
            }
            var stage = new Vector3(boot.SceneX(site.X) - 15f, boot.PlayerPosition.y,
                boot.SceneZ(site.Z) - 15f);
            bool found = false;
            Vector3 ground = stage;
            float elapsed = 0f;
            // Keep the player near the previous ground altitude so the server's player-relative vertical
            // streaming band includes the new ground. The downward ray, not the body, starts 48 m higher.
            // If the terrain is much lower, lower the streaming request in bounded steps; a real floor
            // collider and dry grounded state must still be observed before any image is accepted.
            while (elapsed < 60f)
            {
                Vector3 streamPose = stage - Vector3.up * (16f * Mathf.Min(4, Mathf.FloorToInt(elapsed / 12f)));
                pc.SetCapturePose(streamPose, 45f, 8f);
                if (TryCaptureFloor(pc, streamPose + Vector3.up * 48f, 140f, out var hit))
                {
                    ground = hit.point + Vector3.up * 0.15f;
                    found = true;
                    Debug.Log($"[Capture] Veyl ground={ground} streamPose={streamPose} elapsed={elapsed:F2} "
                        + $"pendingMeshes={boot.PendingMeshCount} cinematic={boot.CinematicCameraActive}");
                    break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!found)
            {
                Debug.LogWarning("[Capture] Veyl view unavailable: no streamed safe ground at the approach.");
                _failedViews++;
                yield break;
            }
            pc.SetCapturePose(ground, 45f, 8f);
            yield return WaitUntil(() => pc.IsCaptureGrounded, 12f);
            if (!pc.IsCaptureGrounded || pc.IsHeadUnderwater() || boot.Health <= 0f)
            {
                Debug.LogWarning("[Capture] Veyl view skipped: player did not settle alive on dry ground.");
                _failedViews++;
                yield break;
            }
            boot.SetCaptureEnvironment(0.30f);
            yield return new WaitForSecondsRealtime(ChunkSettle);
            pc.SetLookAngles(45f, 8f);
            yield return Capture(Path.Combine(dir, "veyl_approach.png"), cleanWorld: true);
            File.WriteAllText(Path.Combine(dir, "concept-view-context.txt"),
                "Actual Unity player rendering; scripted pose/visual-time setup, not a player journey.\n"
                + $"Veyl target {site.X}, {site.Z}; player {boot.PlayerPosition}; image {Screen.width}x{Screen.height}.\n");
            yield return CaptureVaultInterior(boot, pc, site.X, site.Z, ground.y, dir);
        }

        private IEnumerator CaptureVaultInterior(GameBootstrap boot, PlayerController pc,
            float siteX, float siteZ, float surfaceY, string dir)
        {
            // Discover the real descending staircase from replicated shapes. This is scripted capture
            // placement, never a navigation test; no generator origin or guessed chamber floor is used.
            int cx = Mathf.FloorToInt(boot.SceneX(siteX)), cz = Mathf.FloorToInt(boot.SceneZ(siteZ));
            Vector3Int stair = default;
            int highest = int.MinValue;
            bool IsStair(Vector3Int cell) => boot.World.TryGetBlock(cell.x, cell.y, cell.z, out var block)
                && !block.IsAir && BlocksBeyondTheStars.Shared.World.ShapeCode.ShapeOf(
                    boot.World.GetShape(cell.x, cell.y, cell.z)) == (int)BlocksBeyondTheStars.Shared.World.BlockShape.Stairs;
            for (int x = cx - 10; x <= cx + 10; x++)
            for (int z = cz - 10; z <= cz + 10; z++)
            for (int y = Mathf.FloorToInt(surfaceY) - 10; y <= Mathf.FloorToInt(surfaceY) + 6; y++)
            {
                var cell = new Vector3Int(x, y, z);
                if (y < highest || !IsStair(cell)) continue;
                // Prefer the middle of a row with at least three stair cells.
                bool center = IsStair(cell + Vector3Int.left) && IsStair(cell + Vector3Int.right);
                if (y > highest || center) { stair = cell; highest = y; }
            }
            int rows = 0;
            Vector3Int direction = default;
            var steps = new[] { Vector3Int.forward, Vector3Int.back, Vector3Int.left, Vector3Int.right };
            while (highest != int.MinValue && rows < 32)
            {
                bool lower = false;
                foreach (var step in steps)
                {
                    var next = stair + step + Vector3Int.down;
                    if (!IsStair(next)) continue;
                    stair = next; direction = step; rows++; lower = true; break;
                }
                if (!lower) break;
            }
            if (rows < 16)
            {
                Debug.LogWarning($"[Capture] Vault interior unavailable: only {rows} descending rows observed.");
                _failedViews++;
                yield break;
            }
            Vector3 candidate = (Vector3)stair + (Vector3)direction * 5f + new Vector3(0.5f, 0.15f, 0.5f);
            if (!TryCaptureFloor(pc, candidate + Vector3.up * 2f, 3f, out var hit))
            {
                Debug.LogWarning("[Capture] Vault interior unavailable: no observed floor collider at the landing.");
                _failedViews++;
                yield break;
            }
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pc.SetCapturePose(hit.point + Vector3.up * 0.15f, yaw, -10f);
            yield return WaitUntil(() => pc.IsCaptureGrounded, 12f);
            if (!pc.IsCaptureGrounded || pc.IsHeadUnderwater())
            {
                Debug.LogWarning($"[Capture] Vault interior unavailable: grounded={pc.IsCaptureGrounded}, "
                    + $"pendingMeshes={boot.PendingMeshCount}.");
                _failedViews++;
                yield break;
            }
            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(dir, "veyl_vault.png"), cleanWorld: true);
        }

        private static bool TryCaptureFloor(PlayerController pc, Vector3 origin, float distance, out RaycastHit floor)
        {
            floor = default;
            float nearest = float.PositiveInfinity;
            // A ray from above the held setup pose hits the player's capsule first. Inspect all hits;
            // rejecting only that first hit would never reach an already streamed floor underneath it.
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform == pc.transform || hit.collider.transform.IsChildOf(pc.transform)
                    || hit.normal.y < 0.7f || hit.distance >= nearest) continue;
                floor = hit;
                nearest = hit.distance;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        /// <summary>Surface-only capture for one forced planet type (<c>-planet &lt;key&gt;</c>): start a fresh world
        /// pinned to that planet type, take off and land straight back on it (to get out of the onboarding cockpit
        /// seat), then step the on-foot player out of the hull onto open terrain and shoot <c>surface_&lt;key&gt;.png</c>.
        /// The fresh world spawns the player SEATED at the helm, whose pilot view re-locks the camera to the cockpit
        /// every frame — so a bare placement can't leave it. The short EnterSpace→LeaveSpace round-trip (same as the
        /// main planet_surface shot) drops the player "inside the hull" on the surface, out of the seat.</summary>
        private IEnumerator CapturePlanetSurface(AppShell shell, string dir)
        {
            // A distinct world name per planet so each run is its own fresh save (no leftover state between types).
            string worldName = _worldName + "_" + _planet;
            var opts = new WorldCreationOptions { StartPlanetType = _planet };
            shell.StartSingleplayerWorld(worldName, _seed, creativeUnlockAll: true, creativeAllShips: true,
                creativeKit: true, sandbox: true, worldOptions: opts);

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError($"[Capture] World '{worldName}' did not start (bundled server missing?). Skipping {_planet}.");
                yield break;
            }

            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // Get out of the onboarding cockpit seat: take off and land straight back on THIS planet, exactly like the
            // main planet_surface shot. Without this the pilot view keeps the camera locked to the helm and the shot
            // comes out as the cockpit interior even though placement moved the transform. LeaveSpace drops the player
            // "inside the hull" on the surface, out of the seat, so the terrain-aware placement below can step out.
            // Only needed when we actually spawned aboard: a REUSED save resumes wherever the previous run left the
            // player — on foot outside since #401 (quit saves the live position) — and EnterSpace is refused on foot
            // (the "board your ship first" toast), so the round-trip must be skipped there. The Aboard flag arrives
            // with a later PlayerState packet, so give it a moment rather than sampling it once — a fresh world read
            // false here and skipped the round-trip, leaving the camera seat-locked (the ocean cockpit frame).
            yield return WaitUntil(() => boot.Aboard, 8f);
            if (boot.Aboard)
            {
                boot.Network.SendEnterSpace();
                yield return WaitUntil(() => boot.InSpace, 25f);
                yield return new WaitForSecondsRealtime(ChunkSettle);
                boot.Network.SendLeaveSpace();
                yield return WaitUntil(() => !boot.InSpace, 25f);
                yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
                yield return new WaitForSecondsRealtime(ChunkSettle);
            }
            else
            {
                Debug.Log($"[Capture] {_planet}: resumed on foot (not aboard) — skipping the take-off round-trip.");
            }

            // Step the on-foot player out of the hull onto REAL terrain near the ship, facing back at it. The
            // terrain-aware placement raycasts down to the actual surface (no blind offset), so the player isn't
            // dropped through the floor / off a floating island / into water — the exact failures that wrecked the
            // fungal/skylands/ocean shots on the first run. Then we wait until the player has actually settled on
            // solid, DRY ground while ALIVE; if that never happens (tiny island / all-water world), we SKIP the
            // shot rather than write a broken frame.
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc == null)
            {
                Debug.LogWarning($"[Capture] {_planet}: no PlayerController — skipping shot.");
                yield break;
            }

            // Anchor on the SHIP, not the player: on a reused save the player resumes wherever the previous run
            // placed them (out on the terrain), and anchoring there would drift the shot another ring further out
            // every regeneration. ShipPosition comes from the landed-ship placement; player position is the
            // fallback for a fresh world where it hasn't arrived yet (the player is inside the hull there anyway).
            var p = boot.PlayerPosition;
            var anchor = boot.ShipPosition ?? new Vector3(p.x, p.y, p.z);

            bool placed = false;
            bool ready = false;
            float t = 0f;
            while (t < CaptureReadyTimeout)
            {
                // Retry placement until a near chunk's collider exists (raycast hits); once placed, stop moving
                // the player so gravity can settle it and isGrounded can latch.
                if (!placed)
                {
                    placed = pc.PlaceForCaptureNear(anchor, pitch: 18f);
                }

                bool alive = !boot.AwaitingRespawnConfirm && boot.Health > 0f;
                if (placed && pc.IsCaptureGrounded && !pc.IsHeadUnderwater() && alive)
                {
                    ready = true;
                    break;
                }

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.Log($"[Capture] {_planet}: ready={ready} placed={placed} aboard={boot.Aboard} grounded={pc.IsCaptureGrounded}");
            if (!ready)
            {
                Debug.LogWarning($"[Capture] {_planet}: no safe dry footing near the ship (placed={placed}) — skipping shot.");
                yield break;
            }

            // Pin clear-weather noon so the gallery is consistently bright regardless of this world's spawn
            // time-of-day (some land on the night side) or weather roll (rain/overcast). Capture-only; no effect
            // on normal play. The player is already placed, so its longitude offset is final.
            boot.SetCaptureEnvironment(0.5f);

            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(dir, "surface_" + _planet + ".png"));
        }

        /// <summary>Output folder: an explicit -shotOut, else <c>&lt;repo&gt;/docs/screenshots/&lt;lang&gt;</c> in the
        /// editor, else <c>persistentDataPath/docs/screenshots/&lt;lang&gt;</c> in a player build (the ps1 passes -shotOut).</summary>
        private string ResolveOutDir()
        {
            if (!string.IsNullOrEmpty(_outDir))
            {
                return _outDir;
            }

#if UNITY_EDITOR
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")); // dataPath = <repo>/client/Assets
            return Path.Combine(repo, "docs", "screenshots", _lang);
#else
            return Path.Combine(Application.persistentDataPath, "docs", "screenshots", _lang);
#endif
        }

        private IEnumerator Capture(string path, bool cleanWorld = false)
        {
            var boot = FindAnyObjectByType<GameBootstrap>();
            if (_concepts && boot != null && !boot.InSpace)
            {
                // Give the actual streamed world a quiet interval before assessing missing forms or
                // materials. A timeout is an unavailable view, never an apparently completed capture.
                float started = Time.realtimeSinceStartup, quietSince = -1f, nextRecord = 0f;
                int epoch = boot.WorldEpoch;
                var evidence = new System.Text.StringBuilder();
                bool ready = false;
                while (Time.realtimeSinceStartup - started < 120f)
                {
                    float now = Time.realtimeSinceStartup;
                    bool quiet = boot.WorldReady && boot.World != null && boot.World.Chunks.Count > 0 && boot.PendingMeshCount == 0
                        && boot.ChunkMeshFailureCount == 0 && boot.TimeSinceLastChunk >= 0.6f
                        && !boot.CinematicCameraActive && !boot.VegaPrologueActive
                        && InputMap.OwnsVerificationInput(_input);
                    if (boot.WorldEpoch != epoch) { quietSince = -1f; epoch = boot.WorldEpoch; }
                    if (!quiet) quietSince = -1f;
                    else if (quietSince < 0f) quietSince = now;
                    ready = quietSince >= 0f && now - quietSince >= 1.5f;
                    if (now - started >= nextRecord || ready)
                    {
                        evidence.AppendLine($"elapsed={now - started:F2} pending={boot.PendingMeshCount} "
                            + $"dirty={boot.DirtyChunkCount} building={boot.MeshBuildsInFlight} uploading={boot.PendingMeshUploads} "
                            + $"baking={boot.ColliderBakesInFlight} assigning={boot.PendingColliderAssignments} "
                            + $"failures={boot.ChunkMeshFailureCount} epoch={epoch} ready={ready} "
                            + $"player={boot.PlayerPosition} cinematic={boot.CinematicCameraActive} prologue={boot.VegaPrologueActive}");
                        nextRecord = now - started + 1f;
                    }
                    if (ready) break;
                    yield return null;
                }
                evidence.AppendLine($"passed={ready}; scripted pose and visual environment; not a gameplay journey.");
                File.WriteAllText(Path.ChangeExtension(path, ".readiness.txt"), evidence.ToString());
                if (!ready)
                {
                    _failedViews++;
                    Debug.LogWarning($"[Capture] Skipped {path}: scene did not settle before the capture deadline.");
                    yield break;
                }
            }
            // Never catch the VEGA onboarding/greeting dialog in a frame — a fresh world queues her intro
            // lines right at spawn. (No-op on the menu, where no panel exists yet.)
            FindAnyObjectByType<VegaPanel>()?.DismissSpeechForCapture();
            // Likewise the single-line HUD toast: whatever arrived last ("Data fragment recovered!",
            // "Mode: … · PvP: …", space-return notices) would linger into the shot — LastMessage never
            // auto-expires. HudUi copies LastMessage → label only on its 10 Hz Refresh tick
            // (RefreshInterval 0.1 s), so outwait one full interval before reading the frame back.
            FindAnyObjectByType<GameBootstrap>()?.ShowMessage(string.Empty);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForEndOfFrame(); // let the pipeline finish the frame before reading it back
            var hud = cleanWorld ? FindAnyObjectByType<HudUi>() : null;
            bool restoreHud = hud != null && hud.SetCaptureVisible(false);
            yield return new WaitForEndOfFrame(); // the disabled HUD must be absent from the composited frame
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture(); // full composited frame, incl. the overlay HUD
                File.WriteAllBytes(path, tex.EncodeToPNG());
                if (_concepts) WriteVoxelProbe(path);
                Debug.Log($"[Capture] wrote {path}");
            }
            catch (Exception e)
            {
                _failedViews++;
                Debug.LogWarning($"[Capture] failed {path}: {e.Message}");
            }
            finally
            {
                if (hud != null)
                {
                    hud.SetCaptureVisible(restoreHud);
                }
                if (tex != null)
                {
                    Destroy(tex);
                }
            }
        }

        private static void WriteVoxelProbe(string imagePath)
        {
            var boot = FindAnyObjectByType<GameBootstrap>();
            var pc = FindAnyObjectByType<PlayerController>();
            if (boot?.World == null || boot.Content == null || pc?.Camera == null) return;
            var report = new System.Text.StringBuilder();
            report.AppendLine("Read-only first occupied voxel per viewport ray; cutout alpha and fixture meshes are not resolved.");
            report.AppendLine($"Camera={pc.Camera.transform.position} forward={pc.Camera.transform.forward} seed={boot.WorldSeed}");
            report.AppendLine($"Mesh pipeline pending={boot.PendingMeshCount}: dirty={boot.DirtyChunkCount}, "
                + $"building={boot.MeshBuildsInFlight}, uploading={boot.PendingMeshUploads}, "
                + $"baking={boot.ColliderBakesInFlight}, assigning={boot.PendingColliderAssignments}, "
                + $"failures={boot.ChunkMeshFailureCount}. Voxel data can precede its visible mesh.");
            foreach (float sy in new[] { 0.3f, 0.5f, 0.7f })
                foreach (float sx in new[] { 0.25f, 0.5f, 0.75f })
                {
                    var ray = pc.Camera.ViewportPointToRay(new Vector3(sx, sy, 0));
                    bool found = false;
                    for (float distance = 0.2f; distance <= 40f; distance += 0.1f)
                    {
                        var p = ray.GetPoint(distance);
                        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
                        var id = boot.World.GetBlock(x, y, z);
                        if (id.IsAir) id = boot.LandedShipBlockAt(x, y, z, out _, out _);
                        if (id.IsAir) continue;
                        report.AppendLine($"viewport={sx},{sy} cell={x},{y},{z} block={boot.Content.BlockById(id)?.Key} distance={distance:F1}");
                        found = true;
                        break;
                    }
                    if (!found) report.AppendLine($"viewport={sx},{sy} no occupied voxel within 40 m");
                }
            File.WriteAllText(Path.ChangeExtension(imagePath, ".voxel-probe.txt"), report.ToString());
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
            _quitting = true;
            if (code == 0 && _failedViews > 0) code = 2;
            OnDisable();
#if UNITY_EDITOR
            if (_headless)
            {
                UnityEditor.EditorApplication.Exit(code); // batch/headless editor run
            }
            else
            {
                UnityEditor.EditorApplication.isPlaying = false; // menu run — just leave play mode, keep the editor open
            }
#else
            Application.Quit(code);
#endif
        }
    }
}
