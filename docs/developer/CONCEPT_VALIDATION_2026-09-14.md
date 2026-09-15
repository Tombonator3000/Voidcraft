# Concept upgrade validation — September 14, 2026

This implementation is paused at the user's request, not a claim of completion or concept fidelity.
The full scope remains in [TODO.md](../../TODO.md). All launches use isolated profiles so installed saves
are not used or migrated. Local validation players are not official distribution builds.

## Current checkpoint

The published draft is commit `fe81a9c` in [PR #10](https://github.com/Tombonator3000/Voidcraft/pull/10).
The earlier `ad1a681` cloud playtest failed five fresh-world Veyl placement cases; packaging was skipped. The eighth
candidate corrects those cases with supported basalt seating and an open half-step approach, preserves
the complete reservation through JSON save/reload, and protects existing content and player edits.
It also corrects periodic wrapped-chunk eviction, navigation detours, aimed station hints, equipment
readability and versioned vault lighting. These corrections are pushed; all four PR CI shards and their
required test fan-in pass. Cloud run [34887583373](https://github.com/Tombonator3000/Voidcraft/actions/runs/34887583373)
has completed successfully: validation and both Windows/Linux packages passed.

The corrected eighth clean CI build has zero warnings/errors. All 135 distinct selected server cases
(including all five previously failing seeds) and 205 Client.Tests pass, with zero skips. The first
eighth build's compile failure and its exact source snapshot are retained separately. Full restored
format verification passes with zero changes across 677 files after three whitespace-only corrections.
The full Linux player builds without C# or shader diagnostics. All 100 Unity cases pass (91 EditMode,
one atlas and eight terrain-seam PlayMode), zero skipped. Its real-input journey failed at hatch navigation;
the first stationary performance sample passes workload validity but misses the frame-time target.
Nine settled visual captures are available; the forward cabin view and complete journey remain pending.
Generator and save tests do not establish physical controller
access or visual quality. The frozen eighth build source manifest is
`8fbaa342ca9f34b90f10056b2869db33a9fcf3454ece9da6c530b6455a7e5172`, over commit `ad1a681`.

## Identity

- Working branch: `feat/concept-visual-upgrade` from playable
  `9feb2f38feee0810f163a003fffb83532d287a0c`.
- Unity 6000.4.9f1 / URP 17.4.0; .NET SDK 10.0.401.
- First candidate source fingerprint:
  `1161223f88b91ad84d197421f55fc7f0b22280ff5da8c3b324246cf31a3fc531`.
- First frozen payload manifest SHA256:
  `fc5fc313adcf8723706ef023792a84a646c933b1c1b005cd49402ab21ce13d1b`.
  All 262 payload files still matched after captures and measurements.
- Evidence directory: local ignored `artifacts/gauntlet-candidate/`; second candidate evidence belongs
  in its `second/` subdirectory. Baseline is the separate unchanged `VOIDCRAFT-baseline` worktree.

## First candidate

Full Linux build (client, bundled server and launcher) exited 0 with no C# or shader diagnostics.
Five rendered High screenshots completed at actual 1920×1080. These use a creative/sandbox world,
scripted poses and visual-time setup: they establish appearance, not the real opening journey.
The first outdoor framing showed too little ground, so it does not establish terrain material quality.

The unchanged screenshot harness was clamped to 1920×1008 by window decorations. Its distinct performance
run used borderless 1920×1080. Do not call the first screenshot pair pixel-equivalent.

First-source Unity tests found real validation gaps: 29/34 EditMode cases passed; five equipment cases
failed, and the focused atlas PlayMode case failed on the nonexistent `iron_floor` content key.
The second source uses `steel_floor`, corrects the paint sample to an amber part of the stripe, explicitly
normalizes tiny valid bevel triangles after a degeneracy check, and fixes generated material ownership
for inactive parents and edit-mode teardown. The second player built successfully; all 55 EditMode
cases and the focused atlas PlayMode case passed, with no skips. The water reflection emitted six
implicit-gradient warnings across graphics backends. A subsequent source fix makes its screen-texture
samples explicit LOD zero while preserving URP viewport clamping; it requires the third build.

## Second candidate and capture interruption

Second source fingerprint: `2a96ac6372a4daad1d0e51d7b50403d7bf62863d95599740bd897931a67b8674`.
Frozen payload manifest: `1e95a4c2d633250bd3e45552e0fa8e6ac07d1de556f19fdb65f46a0744b85f2c`.
The player is retained at `artifacts/gauntlet-candidate/second/player-second`.

The subsequent capture attempt stopped making progress before its first image. The player had background
execution disabled, so losing focus is a plausible cause; it is not established as a render failure.
The attempt was stopped and is incomplete despite exit code 0. No second-candidate images or performance
pass are claimed. The third source enables background execution only under explicit capture/performance
flags and records focused/unfocused frame counts. Earlier timing runs are focus-sensitive, including the
first candidate's unexplained long stall.

## Authoritative gameplay corrections

The multiplayer correction checkpoint had 74 distinct selected server regressions and 202 Client.Tests passed,
zero skipped. Full CI-filter clean rebuild returned zero warnings/errors; full format verification
returned zero changes across 667 files. This is selected server coverage, not a full server-suite pass.
Earlier canceled tests have no passing status; their raw evidence is preserved separately.

Coverage includes the supported shaped socket, required scanner/standing space, rejected invalid scans,
owned vs guest ship homecoming, correct cargo under another session's cursor, ship-specific replicated
specimens, later co-op visitors acknowledging existing repairs, site-level story deduplication, reload,
fleet changes, and flight/station transitions. Player input/rendering still require a real runtime journey.

## First same-preset performance

Intel Core Ultra 5 225U / Intel ARL integrated graphics, approximately 16 GB RAM, OpenGLCore, 1920×1080,
High, view distance 4, seed 424242, render scale 1, VSync off, unlimited frame cap. Build/test processes
were stopped during measurement. Each row is one wall-clock sample; CPU/GPU attribution is not proven.

| Build / phase | Average ms | p95 ms | p99 ms |
|---|---:|---:|---:|
| Unchanged baseline / idle | 68.05 | 88.69 | 103.71 |
| Unchanged baseline / forward input | 61.00 | 77.29 | 86.71 |
| First candidate / idle | 65.23 | 84.37 | 100.94 |
| First candidate / forward input | 67.57 | 80.73 | 95.49 |

The candidate's forward-input phase included a 2847.7 ms stall; its cause is unconfirmed. About 15 FPS
fails the 60 FPS target. No displacement was recorded in these old probes, so “forward input” does not
mean verified chunk traversal. The second probe records physical movement, aboard/space state, visited
terrain chunks, mesh backlog and valid rendering counters. Unavailable counters are -1, not zero.

Source inspection found the visor UI camera inherited renderer 0 and its full-resolution SSAO, depth and
opaque copies. The second source explicitly selects the existing UI-compatible renderer without SSAO,
disables those requests, scene shadows and post-processing. Savings require another same-High measurement.
Profiler counters follow the official [Unity 6000.4 rendering profiler documentation](https://docs.unity3d.com/6000.4/Documentation/Manual/ProfilerRendering.html).

## Third player: completed capture, incomplete performance gates

Source SHA256: `f4f21d5962a98f00107ef4aee9b62470b47b0469fbaba385f4c246360df8daa8`.
Payload manifest SHA256: `bf7296f35be6e66b8d3b903ccdf2c92353ae2e8652222889281e9560cc5616f4`.
The complete Linux build reported zero C# and shader diagnostics. All 55 EditMode and one atlas PlayMode
cases passed, zero skipped. Seven High captures completed at actual 1920×1080 with no runtime errors or
compressed-audio warnings. The window view is clear and HUD sharper. Terrain remains highly saturated;
the Veyl approach is occluded. These images do not establish concept fidelity.

Both performance attempts kept focus for every sampled frame and used the same named High1080/OpenGL
settings. Spawn idle: average 59.86 ms, p95 65.92, p99 67.35; 489/502 frames exceeded 50 ms. The cabin
forward-input phase: average 48.17 ms, p95 57.75, p99 61.91; 423/1246 frames exceeded 50 ms, maximum
113.68 ms. It moved only 1.12 m and remained aboard throughout. End-of-phase Unity allocated memory was
354.67 MB / 353.79 MB (decimal), managed memory 95.01 MB / 96.04 MB. This is not total system memory.
Draw-call counters were unavailable (-1); peak set-pass counts were 243/234.

The distinct terrain attempt failed safe preparation and exited 2. Movement was deliberately disabled:
zero travel and one visited terrain chunk. It is not a traversal pass. Peak dirty queues of 332–409 did
not record ending backlog, in-flight geometry, uploads or collider work, so neither run establishes
settled idle. No SSAO A/B sweep was performed against an unverified workload. The 60 FPS gate remains open.

## Fourth implementation: architecture and queue scheduling

New starter ships use a persisted authored 6×11 m layout with a 2 m aisle, inward-facing stations, safe
medbay-adjacent spawn, deliberate floor/roof lighting and a framed 4×2 m forward viewport. Version-zero
ships retain the original fallback dimensions and local edits through repair, reload and space visits.
New Veyl sites use geometry version 2; omitted/zero and version 1 keep the original 17-cube generator and
markers without restamping. The new allocation is 25×45×57 cells, including a 22 m staircase and an
approximately 27 m tall buried chamber, three frames, suspended Anchor, intact bridge lane and pit-return
stairs. Burial is 26 m: the shorter proposed 10 m burial would have left most of the vault above ground.
Generator/authority tests do not replace actual controller traversal or an eye-height visual review.

The corrected clean CI build returned zero warnings/errors. All 74 selected architecture cases and all
205 Client.Tests passed, zero skipped. The first architecture attempt had one failure: an old helm test
assumed the spawn was within 3 m of the cockpit. The corrected case now verifies rejection at the new
spawn, approaches the actual cockpit, and verifies helm use. An xUnit assertion warning and whitespace
errors were corrected. Final full format verification with restore returned zero changes in 670 files,
without workspace warnings. Initial failed attempts remain separately recorded.

The source also adds five quieter soil/grass surfaces, a real-input acquire/reload journey driver, and
bounded main-thread terrain work. The performance probe now observes every queue stage and requires a
quiet interval before sampling. The fourth player compiled and passed all 60 EditMode cases plus the atlas PlayMode case.
Its four deprecated-API compiler warnings are corrected in the fifth source.
Fourth frozen source SHA256: `78deb25444c4e520c5dc28e6af570d5994701a9796ad2f1eaee8d9f21c04a3c9`.

Fourth payload SHA256: `bdeeedcfd4a3778044cd95e4ff21b1c0e918b8fff1578536bfb6c90b355845fc`.
All 263 payload files remained unchanged after the runtime attempts. Six actual 1920×1080 High captures
completed; the seventh Veyl approach view was unavailable because safe streamed ground was not found.
The wider pale cabin and clear aisle are visible, but a foreground cyan wireframe display remains visually
dominant. Terrain voxel rays positively identify the pink cylinders as `flora_glowvine`, with mud beneath.
The rays inspect loaded voxel data, not cutout alpha or decorative meshes, and do not prove meshing readiness.

The first real-input journey attempt stopped at its 90-second Menu deadline during the ordinary first-run
introduction. No world start, movement, survey action or reload was verified. The requested 1280×720 was
replaced by ordinary settings with actual 1920×1080. The isolated profile and failure evidence are retained.
Fifth source raises this startup deadline to 300 seconds and records startup phase, frame/focus information
and actual display settings without skipping the introduction or altering normal settings.

## Fifth player: clean build, partial real-input journey

Source SHA256: `4acb227f5563eb47faf7591d8c9e1923554ac6e1e466d8a320a2921564bff21d`.
This source adds a 26th authored surface for glowvine: quiet organic body and narrow emitting channels,
while retaining the deterministic species hue and world placement. An aperture-coverage case supplements
the light tests. Terrain and ore cube faces no longer use decorative bevels, which left holes at certain
multi-block corners; manufactured hull and fixture bevels remain. Eight real-atlas PlayMode cases cover
soil/rock/ore junctions, chunk boundaries, stepped ground and retained hull chamfers without collision edits.
Capture sidecars now include all pending terrain queue stages at shutter time.

Earlier timing probes could overlap the VEGA prologue orbit. The player controller deliberately ignores
movement while that cinematic owns the camera. This is a concrete confound, so no fourth performance
comparison was run. Fifth readiness waits for the actual intro/prologue to finish, the menu to close,
unpaused play, and all terrain queue stages to stay quiet for two seconds. Camera position, rotation and
field of view are recorded during readiness and sampling. An idle sample with camera drift, cinematic
or pause overlap, or terrain backlog is rejected, with its evidence retained. The full fifth Linux build completed with zero C# or shader warnings/errors. All 61 EditMode, one atlas
PlayMode and eight terrain-seam PlayMode cases passed, zero skipped. Payload SHA256:
`5077cdbed35fe077ccc2038094dc43ecafb74cd4a93c5eab9e7e7b0f7f5d58c6` (263 files).

Journey attempt 02 completed the ordinary introduction, started a survival world and moved 5.75 m through
the real controller, then stopped at `LeaveHull / physical_navigation_stalled`. The server shut down
cleanly. The last cached PlayerStateUpdate still contained spawn coordinates, but a read-only inspection
of the saved position confirmed the actual movement, accounting for world wrapping. Cached state is not
a continuous movement acknowledgment. The likely harness failure is a downward diagonal capsule sweep
intersecting the deck edge before taking a legal step down; a focused physics correction is being prepared.
No hatch exit, scan, homecoming or restart pass is claimed.

Startup telemetry also found approximately 1 Hz frame pacing in Studio/Splash/Intro/Loading with normal
VSync enabled on this multi-display Linux session. It recovered in-game; this was not a persistent 1 FPS
condition across gameplay. The startup-only VSync on/off/on comparison did not reproduce the 1 Hz condition, but its focus
states differed; this does not justify a default-setting change. Earlier baseline/first/third idle samples all overlapped the prologue at
different times: raw timing differences must not be attributed to optimization gains.

The fifth High probe reached a quiet start after 66.7 seconds: 510 loaded chunks and all terrain work
queues at zero, with the natural prologue finished. Its idle gate then correctly failed: body travel
40.14 m, camera rotation up to 179.7 degrees, field-of-view change 4 degrees, and renewed mesh backlog
peaking at 610. All 677 frames were focused with no cinematic/menu/pause overlap. Native input remained
available during the probe, so input contamination is plausible, not a proven user action. Raw average
44.35 ms is not settled idle evidence; no SSAO comparison was attempted. Next source gives performance
and journey probes explicit exclusive core-input leases, retaining normal gameplay input outside flags.

## Sixth and seventh players: verified code checkpoint, runtime checks pending

A 27th authored surface replaces the cockpit `data_cache` marker's dense cyan grid with a quiet housing
and a small light aperture, preserving block/collision/station identity. New Veyl geometry version 3 has
a three-meter break across the central bridge and a continuous two-meter side route with equally wide
connections. Three ordinary centerline blocks repair the shortcut; the alternate route needs no jump.
Version 2 remains byte-for-byte in its original generation branch, and saved versions 0/1/2 keep their
geometry, markers and player deltas. Added coverage checks reach, inventory consumption, bridge persistence,
and wrapped version-two saved sites. These new cases are not yet run.

Capture now includes two directions through the home aisle and a vault view located through observed
staircase shapes and a real landing collider. A confirmed capture failure was the single floor ray hitting
the held player's own capsule: rejecting that first hit never reached ground. The revised query examines
all hits and excludes the player, while the held body stays nearer the requested ground streaming band.
The full concept sequence can explicitly select a starting planet, recorded separately from same-seed
material comparisons. All these poses remain scripted visual evidence, never player-journey proof.

Weather advice and aimed scanner selection are separate source changes awaiting their own verification.
The latitude seam cabin check now wraps Z consistently with X; dedicated cases cover owned/visiting
cabins and preserve existing exterior bounds. A thinner muted block outline uses one combined mesh
instead of twelve renderers, with owned mesh/material cleanup. The remaining 25 short creature-bank
importers now use the same readable mono settings as the existing 74 creature clips, addressing sampler
warnings without altering audio content. The sixth clean CI-filter rebuild passed with zero warnings/errors. All 95 selected server cases and
205 Client.Tests passed, zero skipped. Full restored format verification changed zero of 674 files,
without warnings. Sixth Unity source fingerprint:
`7fd4b5c39ad75c94032983d3084f84dccca1a2fa8c8dad672a8cf823fbe6930e` (142 implementation paths).
The sixth player compiled cleanly, but four of 76 EditMode cases failed. Atlas and terrain-seam
PlayMode cases passed. One failure exposed unintended authored alpha scaling in color arithmetic; the
common authored-surface boundary now preserves opacity across all 27 surfaces without changing RGB or
physical channels. Three failures were incomplete EditMode singleton/EventSystem setup; corrected
fixtures retain their original selection and disable/restore assertions. The combined correction suite
passed 17/17 cases.

Seventh source SHA256: `3ca23526da8168d543c3f99d8277ed3dc451ec7b2b916d2c6c9ae0858c5769e8`.
Seventh frozen payload SHA256: `1231a6ed7688d3810dde07fd76a293455f6a96abfeb7388ab36db3791702ffdb`
(263 files). Full Linux build: zero C# and shader diagnostics. All 77 EditMode, one atlas PlayMode and
eight terrain-seam PlayMode cases passed, zero skipped; source remained unchanged through validation.
The third actual journey attempt verified physical hatch exit, ordinary scanner selection and 13.4 m
of walking. It then failed at `SignalWalk`, 65 m from the inscription, when the local route planner
returned no waypoints at a tall terrain face. The current 6.75 m planning window requires immediate
progress toward the target; this does not establish that a player cannot take a longer detour.
Core/UI input isolation and graceful shutdown passed. Scan, excavation, shaping, homecoming and
second-process reload were not reached. Raw events, the failure screenshot and the assessment remain
in `seventh/journey-4242-attempt03/`. Complete acquire/reload, current concept captures and valid
performance samples remain pending.

The verified checkpoint is `ad1a681f8007bd9ce447e8acc210b952165c4179`, pushed on the working branch.
[Draft PR #10](https://github.com/Tombonator3000/Voidcraft/pull/10) targets `feat/voidcraft-playable`.
[Cloud playtest run 34882893539](https://github.com/Tombonator3000/Voidcraft/actions/runs/34882893539)
was dispatched for that exact commit. It failed validation: 1830/1835 fast-tier server cases passed,
with the five placement failures listed below. Client tests and both player packaging jobs were skipped;
no downloadable game artifact was produced by this run.

## Seventh runtime review and next corrections

The default seed 424242 capture produced eight views; the rocky seed 4242 sequence produced nine.
Both are High 1920×1080, use isolated profiles and scripted poses, and lack the forward home view.
Most terrain/home shots still had pending mesh work and cannot establish complete landscape geometry.
The rocky `veyl_vault.png` did reach zero work across every mesh/collider stage, with no failures:
the tall frames, suspended Anchor and side route exist, but the Anchor, pit and bridge break are too dark.
The aft home view shows the intended aisle, warm ceiling apertures and useful room fixtures. Off-screen
station prompts, shadowed equipment faces and terrain-looking marker bases still need correction.

The seventh exclusive-input performance attempt held the body, camera and FOV completely still for all
658 sampled frames. SSAO full and POM on applied, and natural onboarding had completed. Nevertheless,
three terrain-work bursts recurred approximately ten seconds apart, peaking at 432 pending items, then
draining to zero; loaded data/object counts stayed at 510. This failed the empty-work idle gate (exit 4).
Its 45.60 ms average is not an accepted steady-render comparison. All frames were unfocused. Source
inspection identifies the ten-second server chunk eviction sweep using unwrapped distance for canonical
seam chunks; the correction needs regression and runtime verification before any savings are claimed.

The broader PR CI found five placement-guarantee regressions missed by selected local coverage:
`tablelands/37`, `ocean/71`, `highland/23`, `rocky/11` and `swamp/67` failed to place the larger Veyl
site. Other test shards, format and workflow/Python checks passed. A safe fresh-world placement fallback
is being corrected; the draft must not be treated as green or releasable.

The next source candidate includes bounded detour navigation, guarded fixture casings and directional
equipment bounce, aimed station prompts, material-aware mining sound/debris and reduced-effect impacts.
Capture input becomes exclusive and concept images require a logged quiet interval; unavailable views
cause a nonzero exit. Veyl geometry version four retains version-three occupancy, shapes and contacts,
while improving interior light sources, path edge lamps and matte Anchor contour bands. Versions zero
through three retain their saved generator behavior. The eighth player now builds cleanly and all 100
Unity cases pass; accepted visual/performance runs remain pending.

Focused eighth-source Unity verification passed 39 distinct EditMode cases across the initial run
(38/39) and the corrected equipment rerun (18/18), with no skips or compiler/shader diagnostics.
The sole failure was a fixture passing raw packed shape `1`, which means a rotated cube, while claiming
to test stairs. The corrected fixture explicitly permits that cube and rejects a properly packed stair.
Production casing logic was unchanged. This focused result does not replace the full player build.

## Eighth build and corrected terrain placement

The full Linux client, bundled server and launcher build passed with zero C# or shader diagnostics.
All 25 changed implementation paths stayed unchanged through the build. The source archive SHA256 is
`a80c51405dcd2617b215dc08d0a30a897b82a2d8f0b2508dd2b8b7a5180cdafe`; the frozen 263-file player
manifest is `e5fdd962e2ed7f4fdd9307fdcb78823187318ce4147fcd129cca8c1be0a75822`.

New Veyl sites first try the existing nearby dry-ground search. On a never-materialized body, a bounded
fallback can build a supported basalt terrace or dry wellhead, with a five-meter-wide approach using
actual half-step stair shapes. The complete cut/fill and approach reservation is stored with the site.
Other content and player changes are checked before stamping, including both world seams. Existing
sites replay their persisted edits instead of running placement or terraforming again. This fallback
is a placement correction; its rectangular foundation alone does not satisfy the basalt landscape concept.

The 135 selected server cases cover all five previously failing planet seeds, visible stair/headroom
cells and ground support, JSON reservation round-trip, malformed-metadata fallback, protected player
edits and repeated actual streaming sweeps at both seams. All 205 Client.Tests pass. Full restored
format passes after three verified whitespace-only corrections. Build/test logs and before/after source
manifests remain under the ignored `eighth/` evidence directory.
The complete Unity run passes 91 EditMode, one atlas and eight terrain-seam PlayMode cases, with zero
skips or compiler/shader diagnostics. Final verification confirms all 25 source paths and all 263
player payload files still match their saved hashes, with no extra payload files.

Journey attempt 04 exited 1 at `LeaveHull / navigation_observed_geometry_unreachable` after 3.33 m of
walking inside the ship. Input isolation, ordinary startup and graceful shutdown passed; hatch exit,
survey actions and reload did not. Saved geometry and collider reports identify two test-driver issues:
the arrival check required a three-dimensional distance tighter than the driver's allowed landing-height
difference, and a center floor ray misclassified a capsule overlapping both stair treads as blocked.
A correction must retain real collision and observed-cell checks; no hull or controller change is justified
by this attempt alone.

The first eighth High/1080p/OpenGLCore idle sample passes workload validity: all 614 frames had a fixed
body/camera/FOV, exclusive input, focus, no menu/pause/cinematic and zero pending terrain work. No mesh
dispatches, uploads or collider assignments occurred over 30.017 seconds; 510 loaded chunks/objects
remained constant. The earlier ten-second bursts are absent in this sample. Average frame time is
48.888 ms (about 20.45 FPS), p95 52.946 ms, so the 60 FPS target still fails. This is not a pure seventh
versus eighth FPS comparison: other source changes and focus differ. The subsequent forward phase moved
4.12 m while aboard and does not establish terrain traversal.

The complete same-player feature sweep passes workload checks in all four runs: 30-second samples,
fixed pose/FOV, full focus and input ownership, 510 chunks, zero terrain work. The default samples bracket
the isolated feature changes; their mean frame times differ by 0.65%, which is observed drift rather
than a statistical confidence interval.

| Eighth High / 1080p configuration | Mean ms | p95 ms |
|---|---:|---:|
| Full SSAO, POM on (A1) | 48.888 | 52.946 |
| SSAO off, POM on (B1) | 43.105 | 46.708 |
| Full SSAO, POM off (B2) | 48.378 | 52.016 |
| Full SSAO, POM on (A2) | 48.570 | 52.498 |

Removing SSAO saves 11.25–11.83% of frame time in this view, but still misses 60 FPS. Removing POM saves
only 0.40–1.04%, close to the baseline drift; disabling it is not justified here. Ordinary defaults
are unchanged. One stationary integrated-GPU view does not establish traversal or other-machine costs.

The rocky seed 4242 capture completed nine of ten requested 1920×1080 views. Terrain, approach, aft
cabin and vault passed their readiness and shutter checks with zero pending mesh/collider work or
failures. They use scripted poses and controlled lighting, not ordinary journey input. The aft cabin
has a clear aisle, warm ceiling apertures and legible manufactured fixture casings; the vault shows the
Anchor contour, overhead runes, bridge break and warm side route. Terrain remains visually busy and the
local basalt landscape is not present in this eighth player. These are partial concept improvements.
The forward cabin pose did not report grounded and was correctly omitted (capture exit 2); a bounded
capture-only pose retry is awaiting ninth-player verification.

## Remaining gates

- Actual architecture, door/tool/glass/material/specimen review and complete real-input journey.
  Repeat relevant code validation if these checks require further source changes.
- Compare actual terrain, cabin and Veyl approach at the same recorded preset; full monumental forms
  and landscape/ship architecture remain open, even when the materials and fixtures pass.
- Real new-player actions and navigation through the survey, plus restart/continuation and observer view.
- Representative terrain traversal, building, cabin and excavated ruin performance. Current High fails.
- Useful shelter/bridge/power scenarios and actionable weather choices remain in scope.
- Successful cloud playtest artifacts and accurate opening steps; the checkpoint and draft PR exist.

## Continuation plan after the user-requested stop

On September 14 the user requested: stop development, merge the work completed so far and retain a plan
for another session. The merge candidate is PR #10 at the verified implementation commit `fe81a9c`,
merged into `feat/voidcraft-playable` as `292e406f72766e4f513ab54b0adcf9f33070332c`. The unfinished ninth candidate is preserved separately on
`wip/concept-upgrade-continuation-2026-09-14`; it is not part of that merge. All local agents and heavy
jobs were stopped. No automatic continuation or new release has been scheduled.

### Preserved state

- Canonical repository: `Tombonator3000/Voidcraft`; local checkout was
  `/home/tombonator3000t/Documents/Codex/VOIDCRAFT`. Fetch the remote branch before resuming.
- Use Unity 6000.4.9f1 / URP 17.4 and .NET SDK 10.0.401. Keep the existing engine and atomic,
  server-authoritative world/save architecture. Use private test profiles; never installed player saves.
- The eighth player has 135 selected server, 205 client and 100 Unity cases passing, a clean full Linux
  build and green four-shard PR CI. Cloud run 34887583373 uses the exact implementation commit and has completed successfully, including
  validation and both Windows/Linux packages. Downloadable artifacts are on that workflow run.
- Ninth code includes bounded basalt clusters/reservations, transient Veyl response networking/rendering,
  five navigation fixtures, safer capture poses and optional render-scale/thread diagnostics.
- Ninth initial clean .NET build: zero warnings/errors. Initial targeted suite: 79/82 passing.
  `VeylLandscapePlacementTests` incorrectly assumed ocean/71 must use `wellhead_v1`; the observed dry
  fallback is `terrace_v1`. The saved test correction retains support/access assertions and adds an
  independent dry-entry check, but has not run. Two versions of
  `VeylSignalResponseTests.PinnedWrappedSite_EmitsCanonicalSurvivingNodes_WithoutMigratingItsGeometry`
  (0 and 4) fail in `PrepareRepair` at line 85 before response assertions; diagnosis is unfinished.
- Exact failed source/logs are retained locally under
  `artifacts/gauntlet-candidate/ninth/dotnet-attempt01-targetedfailure/`. These ignored artifacts are local
  evidence, not files included in the remote branch. Ninth attempt05 scripts and the evidence-coverage
  matrix are also under `artifacts/gauntlet-candidate/ninth/`; no attempt05 has launched.

### Resume in this order

1. Fetch the repository, inspect the preserved WIP branch against the merged checkpoint and fix the two
   wrapped response failures. Distinguish fixture setup from a real server regression; retain old-save,
   authority, exposed-host and world-wrap checks. Rerun the ocean fixture correction too.
2. Validate the complete ninth candidate: clean .NET CI solution build, the prior 135 affected server
   cases plus signal 11 / basalt helper 16 / landscape 8, all Client.Tests (207 expected), full format,
   then fresh shared-library sync, full Linux player and all affected Unity suites. Expected Unity
   count is 102 EditMode plus one atlas and eight terrain-seam cases; use actual runner counts.
   Run only one local heavy job at a time. Preserve the source and player fingerprints.
3. Run fresh ordinary seed-4242 acquisition followed by second-process reload with the existing real
   controller/uGUI driver. The eighth failure was at the hatch after 3.33 m; the ninth center-first stair
   clearance and shared arrival check are unverified. If it fails again, inspect the new geometry and
   trajectory evidence before expanding the planner. Do not change player collision to satisfy a test.
4. Inspect real 1080p terrain, basalt approach, forward/aft cabin and vault captures after every terrain
   queue is quiet. Verify the missing forward cabin view, local column scale and access, Anchor/bridge
   readability, equipment and physical specimens. Keep generated concepts separate from actual gameplay.
5. Verify the new spatial pulse and positional sound from a legitimate repair, including a same-world
   observer, reduced-effects behavior and no reload replay. Existing tests alone do not demonstrate two
   live clients. The current journey result's `response` field only proves the permanent socket/glow/POI.
6. Exercise ordinary bridge repair/crossing, useful shelter during actual hazardous weather and the
   shaped signal connection. Verify advice/protection and saved edits. The signal connection supplies
   the ruin's power scenario; do not invent a global base-power network to satisfy that wording.
7. Resolve measured performance before adding decoration. First compare the same ninth player at
   render scale 1.0 and 0.5 with identical fixed High/1080p pose and optional thread timings; restore
   settings bytes afterward. This is a diagnostic, not a shipping-quality downgrade. A prepared
   graphics Editor query can establish actual SRP-batcher compatibility before changing shader buffers.
   Thread markers may contain GPU waits. Preserve POM until a measured benefit warrants removing it.
   Then measure representative traversal, construction, cabin and vault; the current 60 FPS gate fails.
8. Update TODO, this validation record and the user manual from observed results. Commit the next
   verified increment, open a new PR against the then-current playable branch, and run the project's
   cloud playtest workflow for that exact source. Do not auto-merge or release from this plan alone.

The approved visual target remains a voxel world with detailed modular equipment/home architecture,
large readable basalt forms and a monumental editable Veyl vault. Use only the built-in ChatGPT image
generator for any new concepts. The user explicitly rejected Magnific/credit-service substitution;
the internal tool does not expose an exact selectable “Images 2.5” version.
