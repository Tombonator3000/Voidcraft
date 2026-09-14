# Concept upgrade validation — September 14, 2026

This is an in-progress implementation record, not release approval or a claim of concept fidelity.
The full scope remains in [TODO.md](../../TODO.md). All launches use isolated profiles so installed saves
are not used or migrated. Local validation players are not official distribution builds.

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
The third actual journey attempt is running against this payload with isolated core and UI test input.
Complete acquire/reload, concept captures and valid performance samples remain pending.

## Remaining gates

- Actual architecture, door/tool/glass/material/specimen review and complete real-input journey.
  Repeat relevant code validation if these checks require further source changes.
- Compare actual terrain, cabin and Veyl approach at the same recorded preset; full monumental forms
  and landscape/ship architecture remain open, even when the materials and fixtures pass.
- Real new-player actions and navigation through the survey, plus restart/continuation and observer view.
- Representative terrain traversal, building, cabin and excavated ruin performance. Current High fails.
- Useful shelter/bridge/power scenarios and actionable weather choices remain in scope.
- Verified checkpoint and unmerged PR, followed by authorized cloud playtest distribution.
