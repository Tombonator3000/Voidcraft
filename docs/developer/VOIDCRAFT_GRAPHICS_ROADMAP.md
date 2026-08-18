# Voidcraft graphics roadmap

This is the working plan for improving Voidcraft's rendering without turning the project into a graphics-tech demo or making weak hardware unplayable.

The visual target is **stylised voxel science-fiction with strong planetary identity**: readable Minecraft-like construction at close range, No Man's Sky-like world variety and atmosphere at large scale, and stranger Voidcraft-specific phenomena where the story calls for them.

The shipping client is Unity 6 + URP. Existing systems are the starting point, not things to replace: procedural block atlas, tangent-space normal atlas + cavity AO, GGX/specular response, SSAO, ACES/bloom, biome/system colour grade, water depth tint, procedural clouds, orbit world-map textures, cloud shells, stars/nebulae and the compact system-flight view.

External graphics repositories and demos may be used as **research/reference only**. Re-implement techniques for Unity/C#/HLSL and review the source licence before copying any code or asset. In particular, Three.js/TSL/WebGPU examples are not dropped directly into the Unity client.

## Non-negotiable guardrails

- Gameplay and simulation remain server-authoritative. Rendering code never decides world state.
- Prefer extending an existing renderer over adding a competing second system.
- Every expensive effect has a quality tier and a hard distance/step budget.
- Potato/Low must remain a real fallback, not a broken version of High.
- Reduced Effects disables optional heavy presentation work.
- Keep the block silhouette readable. Micro-detail may fake depth; it must not lie about collision geometry.
- Generated/procedural assets remain the default where practical.
- Validate new rendering work in a real Unity player build, not only headless .NET CI.
- Promote one graphics phase at a time into the playable branch so regressions are easy to isolate.

## Quality budget

| Feature | Potato | Low | Medium | High |
|---|---|---|---|---|
| Current normal mapping / cavity AO | On | On | On | On |
| Surface atmosphere scattering | Cheap | Cheap | Full | Full |
| Orbit atmosphere limb | Cheap | Cheap | Full | Full |
| Billboard surface clouds | Reduced | On | On | On/fallback |
| Parallax micro-relief | Off | Off | 4 layers, ~10 m | 8 layers, ~22 m |
| Volumetric cloud prototype | Off | Off | Off/very reduced | Bounded |
| Shell turf/moss | Off | Off | Near only | Near + denser |
| Triplanar shaped surfaces | Off | Selective | On | On |
| Screen-space/reflection extras | Off | Off | Selective | Full current budget |
| Void anomalies / lensing | Simplified | Simplified | Bounded | Full bounded effect |

Numbers are starting budgets, not promises. Visual gain per millisecond decides whether they survive playtesting.

## Phase 1 — surface atmosphere

**Goal:** make planets feel like they have air rather than a flat coloured background.

Upgrade the existing `AtmosphereDome`; do not add another planet-sky controller. Use the world's authoritative atmosphere density, weather, sky colour and system sun.

Desired cues:

- longer optical path and stronger scattering at the horizon;
- broad Rayleigh-like sky response;
- forward Mie-like solar halo;
- warm low-sun extinction at dawn/dusk;
- denser/thinner atmosphere identities;
- direct sunlight muted by heavy weather;
- zero atmosphere effect on airless bodies, stations and in open space.

**Work:** draft PR #2 (`feat/atmosphere-scattering`). Headless CI green; Unity/visual verification required before promotion.

## Phase 2 — ground-to-orbit continuity

**Goal:** the planet seen after launch should visibly be the world the player just stood on.

Use the same active-world environment identity for the launch body in orbit:

- sky/atmosphere hue;
- atmosphere density;
- system sun colour and real day/night phase;
- cloud tint and cloud-density response;
- warm terminator scattering.

Do not mix this experiment into flight/landing/networking. Keep it presentation-only and attach to the generated home body.

Other system bodies keep their deterministic type-based orbit look until the client receives enough per-body environment data to reproduce their full surface environment honestly.

**Work:** draft PR #3 (`feat/orbit-atmosphere-continuity`), stacked on phase 1. It intentionally leaves `SpaceView.cs` unchanged. Headless CI green; Unity/visual verification required.

## Phase 3 — clouds

Keep the existing procedural surface billboard clouds as the reliable low-cost implementation.

First improve continuity rather than throwing them away:

1. launch-body orbit shell uses the same cloud colour/coverage/weather identity as the surface;
2. storm/cloud transitions should agree between surface sky and orbit presentation;
3. cloud lighting follows the same system sun/terminator;
4. generated patterns remain deterministic per body.

Only after that, prototype bounded volumetric/raymarched clouds for High. The prototype must have:

- fixed maximum ray steps;
- temporal/distance stability;
- horizon/far-field fallback to the cheap system;
- no full-screen cost when clouds are not visible;
- clean off switch through preset/Reduced Effects.

If volumetrics do not clearly beat the current clouds at an acceptable frame cost, keep the billboard system and improve its shapes/shading instead.

## Phase 4 — voxel parallax micro-relief

**Goal:** cracks, ore seams, masonry and panels should show view-dependent depth without changing voxel geometry.

Start from data already generated by `BlockTextureAtlas`: RGB normal + alpha cavity/crevice AO. Use cavity as a conservative pseudo-height source for the first experiment. This avoids allocating a new full atlas just to determine whether POM is worth its cost.

Rules:

- URP only; shipping shader remains fallback;
- Medium: ~4 layers / ~10 m;
- High: ~8 layers / ~22 m;
- atlas ray steps clamped to the current tile so neighbouring block textures never bleed in;
- no POM on cutout foliage or animated lava/fire;
- distance fade to normal mapping rather than an abrupt switch;
- shadow/collision silhouette remains the original voxel face.

If this succeeds visually, generate a dedicated height channel for selected materials rather than globally increasing texture memory.

Best candidates for authored height later: stone, ores, ancient brick, rune stone, industrial hull panels and selected crafted walls.

**Work:** draft PR #4 (`feat/block-parallax`), stacked on phase 2.

## Phase 5 — near-field turf, moss and fur

Add geometry only where it has a strong silhouette/readability payoff.

For terrain:

- top-facing grass/moss blocks near the camera;
- shell/blade density tied to preset;
- deterministic variation from world/block position;
- fade into the ordinary block texture before the player can see popping;
- never blanket an entire streamed world in shell geometry.

The same shell idea may later support short fur on selected creatures, but creature material work is independent from terrain turf.

## Phase 6 — shaped-surface mapping and translucency

### Triplanar mapping

Use on ramps, wedges, carved/organic surfaces where ordinary cube-face UVs visibly stretch. Keep regular cube blocks on the atlas path; they already map cleanly and cheaply.

### Thin translucency

Cheap wrap/back-light response for:

- leaves;
- alien membranes;
- thin ice;
- crystals where appropriate.

This should make flora and strange materials read against the sun without requiring full subsurface scattering.

## Phase 7 — Voidcraft-specific space phenomena

Once normal worlds are coherent, spend graphics budget on things that belong specifically to Voidcraft rather than generic spectacle.

Candidates:

- bounded gravitational lensing / black-hole-like anomalies;
- wormhole or spatial-rift entrances;
- re-entry plasma;
- aurora driven by atmosphere/system conditions;
- anomalous emissive fog and impossible geometry around story locations;
- signal/Anchor phenomena tied to The Sleeper Signal progression.

These should usually be **events or places**, not permanent full-screen effects. Their rarity is part of their impact.

## Floating origin / very large coordinates

Do not add floating origin just because space games commonly need one. The current `SpaceView` deliberately compresses system flight into a bounded local presentation space, so float precision is not presently the limiting problem.

Introduce an origin-shift/double-coordinate layer only if Voidcraft later moves to genuinely large continuous coordinates and measurements show precision jitter in camera, physics or rendering. At that point it becomes architecture work, not a graphics checkbox.

## Validation loop for every phase

1. Keep the feature in its own branch/PR.
2. Run standard Lint + headless CI.
3. Run a real Unity 6 Linux/Kubuntu player build; Windows when the phase is ready to promote.
4. Capture fixed comparison views: day, dusk, night, storm, cave/interior, orbit where relevant.
5. Compare Medium/High against Low baseline for visual gain and obvious artefacts.
6. Check weak-hardware frame pacing before raising defaults.
7. Only then merge upward into `feat/voidcraft-playable`.

The point is not to collect rendering techniques. The point is to make a block world feel vast, atmospheric and strange while remaining stable enough to actually play.
