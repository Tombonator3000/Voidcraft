# VOIDCRAFT — Gauntlet status 2026-09-15

This is the active continuation record for the approved full visual iteration. It complements
[ART_BIBLE.md](ART_BIBLE.md) and keeps concept references, scripted captures and real-player evidence separate.

## Candidate identity

- Branch: `wip/concept-upgrade-continuation-2026-09-14`
- Source base: `b078204aee46cacaf7b23815c951386841642c00`
- Engine: Unity `6000.4.9f1`
- .NET toolchain: SDK `10.0.401`
- Target: `1920x1080`, High, OpenGLCore, view distance 6, render scale 1
- Recorded machine: Intel Core Ultra 5 225U, Mesa Intel Graphics (ARL), 15524 MB
- Latest built visual candidate at this checkpoint: `client/Build/Linux-gauntlet11bh/`

Payload fingerprints for the latest candidate:

- Linux player: `a9a83136f9f1e9bb13e145b651e13a947bbff6d6a9281f92f0791afc397104cb`
- Bundled server: `01647fa8d70e00434795aa1d7a8608adc1ed1ff67695466179814b9dabc30603`
- `Client.Core.dll`: `e273eb72e5105d26206d1ce50c6e58b3d7cc7571474d161d299799e4b32e0a7d`
- Source working-tree fingerprint (excluding this status record, TODO and nested local test output):
  `1ec81eed78ca97312858bd7ab32aedd220c988a92fd392a5a68763e70b254127`

The build is a local evidence payload, not a release artifact. The working tree contains unrelated
pre-existing WIP and remains unstaged until the feature checkpoint is ready for review.

## Journey gate

Status: **PASS on the bounded test profile; target-profile pass remains open** on seed `4242`.

Evidence comes from the ordinary input driver, not scripted pose or teleportation:

- Acquisition: `/tmp/voidcraft-journey-gauntlet11ax-windowed/result.json`
- Reload: `/tmp/voidcraft-journey-gauntlet11ax-windowed-reload/result.json`
- Acquisition result: `status=acquired`, `RewardReply`, physical hatch exit, surface/core scan,
  excavation, shaped placement, response, own-ship homecoming, one reward and 80 ore hits.
- Reload result: `status=passed`, `ReloadObserve`, same specimen and one reward without duplication.
- The reload comparison uses authoritative wrapped surface distance at the torus seam; it does not
  move the player or substitute a fake position.
- The completed run used a fresh profile with seed `4242`, `1280x720`, Windowed and VSync off so the
  ordinary input driver could reach the world. A fresh default `1920x1080` borderless/VSync run stayed
  at roughly 1 FPS in Loading and timed out; this is retained as a target-profile performance blocker,
  not counted as a journey pass.
- The final `11bh` art payload was rebuilt after the `11ax` journey evidence. A final target-profile
  journey rerun is still required before this candidate can be called fully accepted.

## Build and code verification

- Clean .NET solution build with `-warnaserror` and Windows targeting enabled: **PASS**, 0 warnings,
  0 errors.
- Targeted Veyl server placement/survey tests: **46/46 PASS**.
- Unity EditMode result `client/TestResults/gauntlet-20260915-editmode-r6.xml`: **106/106 PASS**,
  0 skipped.
- Unity PlayMode result `client/TestResults/gauntlet-20260915-playmode-r5.xml`: **10/10 PASS**,
  0 skipped. This includes the atlas and terrain-seam cases.
- The earlier full `run-tests.sh --suites All` runner invocation ended without a retained summary after
  a long CPU-heavy testhost run; it is not counted as a pass. Unity test counts must likewise come from
  the result XML, not from the launcher exit alone.

## Audio gate

Status: **UNAVAILABLE**.

The Treblo session was not available in this environment, so no substitute generation service was used
and no placeholder MP3 is claimed. `ClientMusic` now has the `VeylSignal` context and prefers
`music_voidcraft_signal_treblo` before cave/planet music, with existing fallback, loop, volume and crossfade
rules preserved. When the private track is delivered it belongs at
`client/Assets/Resources/music/music_voidcraft_signal_treblo.mp3` with its Unity `.meta`.
See [MUSIC_TRACKS.md](MUSIC_TRACKS.md).

## Visual evidence types

Approved concept references are normative targets, not runtime proof:

- [Planet surface](../concepts/2026-09-14/01-planetoverflate.png)
- [Ship as home](../concepts/2026-09-14/02-skipet-som-hjem.png)
- [Veyl anchor](../concepts/2026-09-14/03-veyl-ankeret.png)
- [Concept studies](../concepts/2026-09-14/04-chatgpt-concept-studies.png)

The latest scripted capture suite is `/tmp/voidcraft-concept-suite-11bh-final/`, produced from a
fresh capture world with seed `424242`. It records readiness files and voxel probes beside each image.
The latest real-player journey frames are under
`/tmp/voidcraft-journey-gauntlet11ax-windowed/` and are intentionally evaluated separately.

The scripted capture path now supports a unique `-captureWorld` name, suppresses HUD only for explicitly
clean concept frames, and records queue settling before capture. HUD-on frames remain available for runtime
readability evidence. The clean captures still include the first-person held device because that is part of
the ordinary camera, and the rocky seed has a water-heavy horizon near the landing pad; neither is hidden.

## Visual matrix

`PASS` means concept-near and captured on the target preset. `FAIL` means a real frame exists but the
concept gap is visible. `UNVERIFIED` means no representative frame has been captured on this candidate.

| Context | Evidence | Queue / grounding | Status |
|---|---|---|---|
| rocky / varied / highland / skylands | `terrain_materials.png`, real journey hatch/surface frames | readiness passed; rocky frame has water-heavy horizon | FAIL |
| ice | no current candidate frame | unavailable | UNVERIFIED |
| desert / salt flats | no current candidate frame | unavailable | UNVERIFIED |
| lava / ashen | no current candidate frame | unavailable | UNVERIFIED |
| toxic / fungal / corrupted | no current candidate frame | unavailable | UNVERIFIED |
| ocean | no current candidate frame | unavailable | UNVERIFIED |
| verdant / jungle / forest / savanna / swamp | no current candidate frame | unavailable | UNVERIFIED |
| crystal | no current candidate frame | unavailable | UNVERIFIED |
| ship forward / aft | `ship_home_forward.png`, `ship_home_aft.png` | readiness passed; grounded aisle | FAIL — useful structure is present, but current framing/material contrast is behind concept |
| space / orbit | `space_flight.png` | flight capture; no terrain grounding required | UNVERIFIED |
| cave | no current candidate frame | unavailable | UNVERIFIED |
| Veyl approach / vault | `veyl_approach.png`, `veyl_vault.png`, real journey frames | readiness passed; observed floor/stair placement | FAIL — hierarchy reads, but broad surfaces and lighting remain too dark/flat |
| station / finale | no current candidate frame | unavailable | UNVERIFIED |

The current art pass changes rocky lowland material to basalt/stone, reduces natural atlas micro-noise,
deepens the rocky atmospheric base, lowers non-shell bloom/exposure, suppresses flora glow in the rocky
family and gives crystal-like outcrops a darker blue-grey material with restrained emission. These are
measurable candidate changes, not an acceptance claim. The remaining visible gap is concentrated in
concept-level atmosphere/lighting depth, ship material richness and the breadth of the un-captured biome
matrix.

## Performance gate

Status: **FAIL / UNVERIFIED**.

The same-machine High/OpenGLCore run against the preceding `Linux-gauntlet11ao` payload recorded real
frames but was invalid for acceptance because every sample was unfocused and the idle scene was not stable:

- 1920x1080, High, OpenGLCore, view distance 6, render scale 1, vSync 1
- 342 idle frames over 20.0065 s
- average `58.4985 ms`, p95 `65.8953 ms`, p99 `82.9488 ms`, max `89.1578 ms`
- 338 frames over 50 ms, 0 over 100 ms
- focused frames `0`, unfocused frames `342`
- terrain phase not prepared; no traversal/building measurement

No performance gain or gate pass is claimed. A valid focused run with representative surface traversal,
ship interior, cave/Veyl-vault and building/mesh update states remains required.

## Next Gauntlet slice

1. Re-run the journey/reload candidate on `11bh`, first with the bounded profile and then on the target
   `1920x1080` High/OpenGLCore profile after the frame-pacing issue is resolved.
2. Capture the remaining representative planet families and record skips as unavailable rather than inventing frames.
3. Run a focused performance probe with documented focus and settled-camera evidence; only then compare against
   the `16.67 ms` / `20 ms` p95/p99 gates.
4. Keep iterating the highest-impact visual mismatch from the runtime frames: indigo/amber atmosphere
   separation, ship ceramic/graphite detail and Veyl vault value hierarchy.
5. Keep the Treblo track explicitly unavailable until the private source is reachable.
