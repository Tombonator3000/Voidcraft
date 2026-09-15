# Art Bible — Voidcraft

Status: **normative.** The single style reference for any visual work (blocks, ships, planets, UI,
effects). The concept target and current implementation are explicitly distinguished below. Related: [PROFESSIONAL_LOOK_GAP_ANALYSIS.md](PROFESSIONAL_LOOK_GAP_ANALYSIS.md),
[ADVANCED_GRAPHICS.md](ADVANCED_GRAPHICS.md), [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md).

## 1. Art direction in one sentence

> **An atmospheric space exploration game: editable voxel landscapes, tactile modular ships,
> warm human refuges and restrained cyan Veyl signals in vast, cool environments.**

The user selected the three September 14 concepts as the implementation target. They are **concept art,
not screenshots or proof of runtime performance**:

- [Planet surface](../concepts/2026-09-14/01-planetoverflate.png): large stepped basalt forms, an ivory/graphite
  modular ship, warm rim light, cool depth haze and a recognizable distant signal landmark.
- [Ship as home](../concepts/2026-09-14/02-skipet-som-hjem.png): ceramic panels, dark structural ribs, a useful
  workbench and sleeping space, warm task lights and a clear route toward the cockpit.
- [Veyl anchor](../concepts/2026-09-14/03-veyl-ankeret.png): a monumental stepped vault, suspended angular
  mechanism, restrained cyan channels, an excavated approach and a meaningful bridge interruption.

Provenance: these existing references were generated through Magnific before the user's correction.
They are retained as approved visual targets. **New image generation must use the built-in ChatGPT image
capability; Magnific is not authorized.** Do not claim an exact model variant without tool evidence.
The later [ChatGPT concept sheet](../concepts/2026-09-14/04-chatgpt-concept-studies.png) was generated with
the built-in tool; its [prompt and provenance](../concepts/2026-09-14/04-chatgpt-concept-studies.md) are retained.
It is a derivative design study, not a game capture.

## 2. Hard style rules

1. **Hybrid geometry.** Editable voxel data remains the truth for terrain, construction, collision and saves.
   Detailed tools and modular ship/ruin fittings may use chamfered meshes. Decorative geometry must fit
   its real footprint and disappear with the owning object. No walkable-looking unsupported surfaces.
2. **Intentional materials.** Broad quiet fields, legible seams and selective wear. Authored physical height
   is independent of paint. The current concept atlas uses 128-pixel tiles, trilinear filtering and 4×
   anisotropy; 256 slots remain unchanged. This resolution is an implementation choice to measure,
   not evidence that the concept's material quality has already been reached.
3. **Useful glass.** Cockpit glazing must support navigation and visible outside depth. Frosted glass may
   identify privacy or machinery, but is no longer mandatory for every window.
4. **Reproducible assets.** Retain the existing code-built scene pipeline. Cached detailed mesh assemblies
   are acceptable; imported authored assets need reproducible source/export and a verified engine handoff.
5. **Linear color space.** sRGB constants cross custom shader boundaries through `ShaderColor.Srgb()`.
   Colour textures use sRGB sampling; normals and packed material maps use linear sampling.
6. **Bilingual.** All player-facing text has EN+DE keys. Controls follow the selected device and remapping.
7. **Quality and comfort.** Expensive effects scale by preset; reduced effects and camera motion choices
   retain their meanings. Test the same preset for visual review and performance claims.

## 3. Color palette

UI system (`UiKit.cs`):

| Name | Value | Use |
|---|---|---|
| UI Cyan | `(0.40, 0.82, 1.00)` | primary accent, borders, highlights |
| Deep Navy panel | `(0.05, 0.12, 0.24, 0.80)` | translucent panel fill |
| Soft White text | `(0.86, 0.93, 1.00)` | body text |
| Success green | `UiKit.Ok` | craft ready, confirmations |
| Health red / Oxygen cyan / Energy amber / Hunger orange | vitals bars | semantic, never reused decoratively |
| Warning Orange | `(1, ~0.6, 0.2)` family | sparks, hazards, engine glow |
| Hazard Red | `(1, 0.12–0.2, 0.08–0.2)` | damage flash, emergency light |

Concept world palette: ivory ceramic, graphite structure, subdued basalt, warm amber task lighting,
cyan only for functional signals/interfaces, cool indigo atmospheric depth. Planet identity may introduce
other colours deliberately; avoid covering a whole frame in unrelated saturated emissive patterns.
`BlockSurfaceLibrary` authors independent colour, height, roughness, metal and light-aperture channels
for 27 common materials, including quiet soil/grass, sparse glowvine channels and a restrained data-cache housing. `BlockTextureAtlas` packs those channels, with explicit legacy fallbacks.

## 4. Planet identity (data-driven)

Every planet type owns a mood — sky + fog follow the system **sun colour** (a red star tints its
worlds), the grade comes from `Sky.GradeFor` (tint × saturation × contrast), clouds from
`planets.json` (`cloudColor`, `cloudDensity`), flora from the per-world random hue
(`FloraTints`, deterministic from seed + location + species):

| Biome | Grade tint | Sat | Contrast | Mood |
|---|---|---|---|---|
| jungle/forest | (0.98, 1.05, 0.96) | 1.12 | 1.05 | lush |
| desert | (1.07, 1.00, 0.90) | 0.95 | 1.12 | warm, dusty |
| ice/frozen | (0.94, 1.00, 1.09) | 0.90 | 1.06 | cold blue |
| lava/volcanic | (1.10, 0.95, 0.86) | 1.05 | 1.14 | hot orange |
| swamp | (0.97, 1.03, 0.95) | 0.85 | 1.03 | muted sickly |
| crystal | (1.04, 0.97, 1.09) | 1.10 | 1.05 | cool sparkle |

New planet types: add data (`planets.json`) + a `GradeFor` entry; never hardcode looks client-side.

## 5. Ship rooms (interior palette)

From the room-identity pass (`GameServerSpaceStructure.PaintStructureAccents` + decor in
`StationDecorView`):

| Room | Floor accent | Light/decor |
|---|---|---|
| Medbay | `medbay_panel` (white/blue, medical cross) | diagnostic housing and contained status display |
| Cockpit | `lab_panel` (cool tech blue) | console + animated screen + holo system map |
| Lab | `lab_panel` | sample instrument and restrained display |
| Ship console | `lab_panel` | chamfered instrument console and narrow cyan inlays |
| Cargo | `cargo_floor` (painted amber/graphite safety strip) | grouped storage cases |
| Workshop | `engine_panel` (graphite + orange paint) | tool rack, work surface and ship-owned survey specimen |
| Quarters | `metal_panel` (dark, calm) | compact berth with fabric and bedding |
| Corridor/walls | `iron_wall` + `strip_light_cyan` rows | emissive cyan strips above the windows |
| Engines | `engine_nozzle` (glowing throat ring) | idle glow even when landed |

Ship/tech blocks keep **aligned seams** — they are excluded from the natural-block variant/rotation
system (`BlockTextureAtlas.VariantKeys` whitelist is natural blocks only).

## 6. Light & post

- ACES tonemapping; the 2026-09-15 candidate uses bloom threshold 1.20 / intensity 0.20 / scatter 0.40
  and base vignette 0.16 (`UrpScenePost`) to keep lamp cores selective while retaining amber task light.
  These values require runtime comparison, not approval by inspection alone.
- Equipment has independent finish properties and follows scene sun/shadows, cabin fill and the headlamp.
  Its palette and mesh data are shared, with narrow signal faces in a separate cached draw.
- The visor UI camera uses the renderer without SSAO and requests no depth/opaque copies, scene shadows
  or post-processing. UI text keeps subpixel chromatic separation and restrained glow.
- Emission is spatially limited: the lens/strip/inscription may emit, its housing must retain visible material.
  Do not flatten a room with broad white emission or treat decorative glow as a substitute for lighting.
- Dynamic vignette is reserved for meaning: blue pulse = low oxygen, red kick = damage; chroma/
  grain bursts only for events (damaged visor, EMP) via `UrpScenePost.Burst`.
- Night is never pitch black (0.20 brightness floor); interiors get the `_Sc_Indoor` fill.

## 7. Feedback ("juice") rules

Aim for readable, timely action feedback, using sound and restrained visual response as appropriate:
mining = crack tint → debris → final-hit flash → tile flies to the hotbar;
crafting/unlock = card pulse + floating label + fanfare; menus fade/rise in 0.14 s;
hotbar selection ticks; hits spark; low resources pulse when comfort settings allow it. New features must wire into these
channels (`MiningFx`, `UrpScenePost`, `UiKit.TransitionIn`, `ClientAudio`/`ProceduralAudio`)
instead of inventing parallel ones.

## 8. Quality checklist (every important object)

- [ ] clear silhouette (readable at a glance, also in the hotbar icon)
- [ ] at least two material zones (e.g. dark housing + bright band)
- [ ] intentional light placement where function needs it; no mandatory glow on every prop
- [ ] a function the design communicates (warning stripes mean hazard, cyan means interface)
- [ ] localized name (DE+EN), icon resolvable via `IconResolver`
- [ ] effects preset-gated / comfort-toggle aware


## 9. Verification against the concepts

Compare actual captures of daylight terrain, ship interior and excavated anchor at 1920×1080. Record
source revision, build identity, preset, seed, procedure and hardware. A scripted capture pose can assess
materials and framing; it does not prove the player's journey, controls or discovery timing.

The working target is 60 FPS at 1080p on the recorded test machine, with p95 frame time ≤16.67 ms,
p99 <20 ms and stalls >50 ms counted. Do not claim this gate passed without measurements of the same
candidate/preset. Current status and remaining concept gaps belong in [TODO.md](../../TODO.md).
