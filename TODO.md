YªçŠx-®éÜj×¢ëiºÚ+Š§j[h‘éÜ¢éíß}½ß]¼ß^<o+^²‰¢¶×# Voidcraft â€” Project Status

The single source of truth for **what is built** and **what is still open**. Design notes and deep
plans live under [docs/](docs/) (committed); the long-range direction is the strategy trio in
[docs/strategy/](docs/strategy/vision.md) (vision Â· mission Â· roadmap); this file is the high-level status. Player-facing operation
(controls, mechanics, editors, commands) is documented in [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md) â€”
keep it current when controls/features change. Last consolidated 2026-06-04.

**Build:** `scripts/build-client.ps1` (Windows) or `scripts/build-client.sh` (Linux) â€” publishes shared libs + bundled server + Unity player.
**Test:** `./scripts/run-tests.sh` â€” currently **1547 server + 194 client passing** (2026-08-09). Locale parity (en/de) is enforced by a test.
CI runs two tiers: PRs skip the tests marked `[Trait("Category", "Slow")]`; pushes to `main` and the release workflow run the full suite. CI builds/runs
tests in Release, and a per-test duration guardrail (`scripts/check-test-durations.py`, PRs only) fails the gate when a non-Slow test exceeds 120 s.
The server suite is sharded across a 4-runner matrix (`scripts/partition-tests.py` + checked-in weights; `Tests passed` is the required fan-in check) â€” PR gate ~4Â½ min.
A PR touching nothing but `data/locales/*.json` runs a single narrow `locale-tests` job instead of the matrix (`scripts/locale-test-filter.py`, ~140 tests).
**Conventions:** English docs/comments; in-game text localized via locale keys â€” EN+DE mandatory-complete,
FR/ES/PT/PL/TR/NL/RU/UK/ZH/JA/KO machine-first-pass, IT community + machine top-up (see docs/developer/TRANSLATION_GUIDE.md); commit to `main` with the
Claude `Co-Authored-By` trailer; OpenAI texture + ElevenLabs sound generation is blanket-approved
(no per-batch gate).

Architecture: Unity 6 (URP since 2026-06-10) client + authoritative .NET 10 server, everything built in
code (no scene authoring). One shared world; MessagePack networking for native clients plus a WebGL JSON
envelope at the WebSocket edge; deterministic seed world-gen; SQLite default persistence with opt-in PostgreSQL.

---

## Voidcraft foundation

First identity/story slice on branch `feat/voidcraft-foundation` (2026-08-07), with GitHub playtest delivery
on `feat/voidcraft-playable` (2026-08-12), built on the AGPL-licensed Blocks Beyond the Stars foundation.
Design and scope: [Voidcraft foundation](docs/developer/VOIDCRAFT_FOUNDATION.md).

**Done**

- Player-facing title and desktop product metadata changed to **Voidcraft** while technical executable,
  namespace and Unity data-folder names remain unchanged.
- New default story pack `voidcraft_awakening` (**The Sleeper Signal**): 13 beats, 6 fragments, 4 memories,
  5 NPC flavour lines and a 4-node finale argument, authored in complete EN+DE locales.
- Fresh-world prologue/intro reframed around an erased ship log and a subsurface signal; original
  `vega_protocol` remains installed and `none` remains available for sandbox play.
- Finale reveal/resolution and contextual insight lines moved out of VEGA-specific server constants and into
  story-pack data; story validation now covers every referenced story locale key.
- Registry, loader and server-story tests updated for the new default and multi-pack compatibility.
- Fork-facing update/news/contribution links now target `Tombonator3000/Voidcraft`; the upstream Official
  Worlds service is disabled by default while explicit self-hosted WorldHost URLs remain supported.
- Fork-specific `voidcraft-playtest.yml` validates the fast .NET tier and produces a portable Windows player
  plus a permission-preserving Kubuntu/Linux tarball, each with its platform-native bundled local server.
  Setup/download instructions and in-archive start guides are committed.
- The first GitHub run compiled with zero warnings and exposed a story-locale test false positive: legitimate
  bracket-led recovered-log prose is now checked with `Localizer.Has` instead of mistaken for `[missing.key]`.
- Bundled Singleplayer/Host now places the active story pack's first unread fragment roughly 20 blocks from
  the starting pad. In Voidcraft this is the recovered VEGA ship trace and immediately unlocks **The signal
  below**; dedicated servers retain the rarer world-seeded scatter.

**Open before a public Voidcraft build**

- Expand the guaranteed opening signal shard into a full terrain/structure **Signal Scar** encounter, then
  tune the first 30 minutes through playtests.
- Add a visible three-way world-creation selector (Sleeper Signal / VEGA Protocol / sandbox).
- Create a distinct logo, menu imagery, Anchor geometry/audio and Voidcraft screenshots.
- Smoke-test the first Kubuntu artifact on physical hardware, including launching Singleplayer and recovering
  the nearby opening signal. See [Voidcraft GitHub playtest](docs/developer/VOIDCRAFT_PLAYTEST.md).
- Create fork-owned hosted-world/release infrastructure before enabling those public services.
- Run the full .NET and Unity suites in a dependency-complete build environment, then package signed release
  clients. The playtest workflow intentionally runs the non-Slow .NET tier and produces an unsigned artifact.

---

## ðŸ“¦ Released versions

Published GitHub Releases (tag = version single-source-of-truth; each tag push builds the Windows installer
trio, pushes the dedicated-server Docker image to GHCR and mirrors the builds to itch.io). Newest first.
Per-item detail lives in the dated work log below. **Since 2026-07 versions are date-based (CalVer)
`YYYY.MM.N`** â€” year.month.counter, valid SemVer2, one-way switch (no `1.0` ever) â€” see
[ADR 0012](docs/developer/adr/0012-calver-date-based-versioning.md); releases up to v0.9.1 used SemVer.

- **v0.6.2** â€” 2026-06-30 â€” *gameplay polish, oxygen tiers & a browser-client start.* **Gameplay fixes** â€”
  shaped blocks (sphere/pyramid/slab) now get masked silhouette icons in the hotbar/crafting instead of plain
  cubes, ladders are climbable, you auto-step onto slabs/stairs without jumping, and mining is tool-gated (stone
  needs a drill â€” granted by the starter kit â€” while soft earth/sand/plants dig bare-handed) (#130, fixing
  #125â€“#128). **Oxygen Tank tiers I/II/III** â€” the dead `oxygen_tank_1` consumable became a worn `+50` component;
  II `+100`, new III `+200` (titanium recipe, blueprint-gated); `MaxOxygen` now takes the **best** tank, not the
  sum (#133, issue #129). **Easier water exit** â€” a real jump impulse + full forward speed lets you mount a
  â‰¤1-block bank instead of bobbing and sliding back (#133, issue #131). **Sealed a station-to-space gap** â€” flora
  cells opening onto the void (see-through *and* walk-through) are dropped via a bounded flood-fill enclosure
  check, on both live placement and void-world station stamps (#134, #135). **Browser client (experimental,
  not yet verified end-to-end â€” [#132](https://github.com/marceld23/BlocksBeyondTheStars/issues/132))** â€” hosted
  **WebGL client + optional PostgreSQL** by Devin Dixon (#116), served at `/play` from the dedicated-server image
  (#123), slim browser start menu (#122); the WebGL player build needed a Velopack compile-guard fix (Velopack is
  desktop-only) plus a `webgl-only.yml` workflow that attaches the WebGL zip to a release without rebuilding the
  desktop installers (#151). **Plus** dev-build packId isolation (#115), de.json duplicate/wording cleanup by
  Maqbool Ahmed (#112), README Download & Play CTA (#113) and a hosting/forks policy in CONTRIBUTING (#117).
- **v0.6.1** â€” 2026-06-28 â€” *experimental macOS, crash reporting & portal downloads.* **Experimental macOS
  client** â€” a `StandaloneOSX` `.app` cross-built on the Linux runner (Mono backend, no Mac hardware), shipped
  as a portable zip; unsigned/un-notarized and **not yet validated on real hardware** (help wanted: [#87](https://github.com/marceld23/BlocksBeyondTheStars/issues/87))
  (#84, #105, #106, #107). **Automatic crash reporting** â€” hardened server tick + opt-in client/server crash
  reports with a PII scrubber (#103). **One-click client downloads from the server portal** for Windows, Linux
  and macOS â€” fetched at container start from the latest GitHub Release (#83 Linux, #107 macOS). **Release-CI
  hygiene** â€” single shared version job, Docker decoupled from the Windows build, concurrency group, Windows
  job-name fix, plus a slim `macos-build.yml` for targeted platform iteration (#105, #106). Dead arcade HTML/JS
  removed + minigame/wiki data moved under `data/` (#104); Playtest issue template (#85); kid-friendly intent
  signalled across README/CoC/Credits/Codex (#109); GitHub-stars community invite (#110). Native Linux is still
  fresh too â€” testers welcome ([#86](https://github.com/marceld23/BlocksBeyondTheStars/issues/86)).
- **v0.6.0** â€” 2026-06-27 â€” *factories, ruins & native Linux.* Claimable **factories** with animated machine
  bays + roster-limited production terminals, mineable **ruins** + standalone **treasure chests**, and **SPS
  access codes** that claim a factory as your own editable base (shared with allies); **native Linux client** â€”
  a real `StandaloneLinux64` shipped as AppImage + Linux Portable.zip, no Wine/Proton â€” the **first community
  contribution**, by Cora de la Mouche (#69); new **Transmuter** station (spare terrain â†’ scarce ore) + the
  **Refinery** expanded into the full metallurgy station (Tier-2 metals, diamond, carbide, reactor fuel) +
  titanium/carbide progression-deadlock fix; bigger ship/station/city editors; longer planet view distance with
  far-chunk vertical LOD + unload; rivers & lava route downhill into sinks with waterfalls/lavafalls;
  per-atmosphere haze; trees scanned as named per-world species; menu attract-scene aligned with the real
  in-game space look.
- **v0.5.0** â€” 2026-06-26 â€” *visual overhaul, browser-free UI & runtime quality.* In-game post stack now live
  (SMAA + SSAO + emissive glow), per-biome cinematic mood LUTs, GGX specular + roughness-aware reflections,
  global brightness control, readable/reflective water + visible sky bodies (#50â€“#56); embedded browser
  (UnityWebBrowser/CEF) removed â€” native Codex/Wiki + all 20 Arcade minigames on a native Canvas2D framework
  (#58); remappable controls via InputMap across on-foot/UI/flight/EVA/vehicle/trade (#60); off-thread chunk
  meshing + analytic normals + mesh reuse + far-chunk distance cull, plus presence-stream interest management
  (AoI) + remote-player snapshot interpolation (#60); usable ship cargo hold â€” manual transfer, capacity,
  stow/take-all, auto-stow-on-board toggle (#62); relicensed MIT â†’ AGPL-3.0-or-later + Contributor License
  Agreement (#59); regenerated marketing screenshots (#63); contributor invite + released-versions index (#48).
- **v0.4.2** â€” 2026-06-23 â€” *early-game survivability + Linux/Proton perf.* Starter food + VEGA "eat" lesson,
  earlier/clearer hunger guidance (#46); `scrap_pistol` starter sidearm + weapon-range fix + 3D line-of-sight
  hiding (#45); VSync decoupled from the quality preset + frame-rate cap (Linux/Proton 30 fps fix, #47);
  README intro rework (#44). All three player issues reported by community tester **Bastian**.
- **v0.4.1** â€” 2026-06-22 â€” *"Caught on Camera" fixes.* Keep wild creatures/planet enemies out of the parked
  ship (#43); stop enemy attack FX following a player aboard (#41); hide surface-only compass + time-of-day
  panel during space flight (#39); feedback is now F1-only, unclickable HUD button removed (#42); automated
  in-game clip recorder tool (#40); Docker Desktop local-test walkthrough (#38).
- **v0.4.0** â€” 2026-06-21 â€” *comms, scale + CI hardening.* Tiered radio reach + live voice chat (#30); default
  player cap raised 4â†’12 (#19) with a 12-player smoke test (#21); optional dedicated-server Docker image (#26);
  in-game menu top-bar split into two rows (#35); Windows security notice; CI hardening (CodeQL compile mode,
  lint + format gates, Action SHA-pinning, Node 24 bumps); Docker pull line prepended to release notes (#37).
- **v0.3.2** â€” 2026-06-21 â€” *space/landing polish + screenshots.* Ranged laser for scan-drones (#13);
  terrain-aware screenshot placement (#14); descent flies to the chosen planet (#12); rear hatch shown as a
  blue energy door in flight (#11); explosion stays / no landing animation after ship destruction (#10);
  per-planet surface screenshot gallery (#8/#9); butler host fix (#5); README TOC + project-status (#6/#15).
- **v0.3.1** â€” 2026-06-20 â€” *release-CI fix.* Strip the Burst `_DoNotShip` folder + mirror the installers to
  itch.io via butler (#4).
- **v0.3.0** â€” 2026-06-20 â€” *community + feedback.* In-game "Spieler Feedback" button â†’ website API; open-source
  "Mach mit" invite + sharper menu/splash backdrop; community health files (Code of Conduct, Security policy,
  issue/PR templates); analyzer-warning cleanup (#3); ai-backend security bumps (#2); PR-workflow docs (#1).
- **v0.2.0** â€” 2026-06-20 â€” *version SoT + installer trio.* Version single-source-of-truth from the tag +
  Windows installer trio (Setup.exe / MSI / Portable); third-party NOTICES shipped with the installer.
- **v0.1.0** â€” 2026-06-20 â€” *first automated release.* GitHub Actions release build + version
  single-source-of-truth working end-to-end (validated: published the initial Windows zip).

---

### â˜… Arcade full? The WebGL menu now says so â€” and points at singleplayer (#936, 2026-08-11, branch feature/arcade-full-notice-936)
When every glitch.fun arcade world is at capacity, the portal's `/api/glitch/session` already answered
with a machine-readable code (`glitch_full`; `no_capacity` when the fleet RAM budget is spent) â€” but the
WebGL client threw the body away and showed the same generic `ui.glitch.arcade_failed` notice for every
failure, indistinguishable from a network hiccup. `GlitchIntegration.RequestArcadeSession` now parses the
portal's `{error, code}` error body and reports the code alongside the description; `AppShell` maps the
two capacity codes to the new `ui.glitch.arcade_full` notice â€” "All arcade worlds are full right now -
please try again in a few minutes. Singleplayer is always open!" â€” in all 14 locales (machine top-up for
the 12 non-EN/DE ones, which also back-filled the two `hud.shape.*` keys #909 had left untranslated).
Every other failure keeps the generic notice, and the server-side join race (`srv.join.server_full`)
is deliberately untouched â€” it is shared with self-hosted servers.

### â˜… Translation PRs get a narrow CI lane: locales-only diffs skip the 4-shard matrix (#922, 2026-08-11, branch ci/locale-only-test-gate)
A PR whose non-doc diff stays inside `data/locales/*.json` now runs one `locale-tests` job (~140 tests,
< 1 min of test time) instead of the 4-runner matrix + `dotnet format`. The affected-test set lives in
`scripts/locale-test-filter.py`: a self-maintaining marker scan for direct locale-table readers
(TestLocales.Load / CreateLocalizer) plus hand-listed indirect consumers; `verify` runs in CI and fails
loudly on stale entries. The set was validated EMPIRICALLY: replace every locale value with junk
(placeholders kept), run the full fast tier â€” only marker-visible classes fail, and Client.Tests passes
195/195 against destroyed locales, so skipping it is proven safe. Two method gotchas recorded for
regeneration: appending junk is too weak (Contains() assertions survive; replacement is required), and
NpcHintTests' locale-consuming methods are Slow-tier (never in the PR gate to begin with) â€” it stays in
HAND_EXTRAS anyway in case those methods ever lose the trait. `detect-doc-only.yml` gained a
`localesonly` output; the `tests-passed` fan-in refuses the vacuous both-skipped state on such PRs.
Coverage guarantee unchanged: pushes to main and release.yml still run the COMPLETE suite.

### â˜… Space HUD: the cargo readout stopped overprinting the hull bar (#915, 2026-08-10, branch fix/space-hud-cargo-overlap)
The flight view's top-left `Cargo: N` label was drawn straight across the on-foot HUD's **Hull** bar (and
`Oâ‚‚: n%` did the same on an EVA). The two live on **different canvases** â€” `SpaceView`'s overlay at
`sortingOrder 12` over `HudUi` at `10` â€” but both are built at the same 1536Ã—864 reference with the same
`Expand` mode and UI-scale divisor, so they share one coordinate space and the overlay's text wins the
depth test. The label sat at a hard-coded `y = -160`, which is exactly where the vitals panel's content ends
**on foot**; `RefreshVitals` appends the hull + shield rows and grows the panel from 116 to 196 px whenever
`ShipCombat != null` â€” precisely the space-flight state â€” so the first appended row landed in the reserved
slot. Guaranteed in flight, impossible on foot, which is why it survived so long.
**Fix, two parts.** `HudUi` now publishes its live panel edge as `VitalsBottomY` and `SpaceView` parks its
readout below it every frame, so the block follows whatever rows the panel is showing instead of assuming a
fixed edge â€” the bug class is gone, not just this instance. And **hull + shield moved into the flight HUD**
as real gauges: the vitals panel drops its ship rows while piloting (the same step-aside the compass and the
time-of-day panel already make), and the flight view draws its own hull/shield bars under the cargo line, in
the same column as the suit's bars so the two read as one stack. A pilot gets the fill back â€” `HULL 240`
alone never said 240 *of what* â€” and the numbers stay, printed on the bars. The old duplicate `HULL/SHD`
text is gone from the instrument line, which is back to `SPD/THR/HDG`. The bars inherit the panel's warning
behaviour (blink toward red below 10 %, clearing at 15 %, plus the 2.5 s `vitals_warning` cue) and add a
guard the panel lacked: a system the ship doesn't *have* (`Max == 0`) never alarms, so a shield-less ship
stops screaming. On anÛ5çkh‘éì¶»§q«^uÍ•Ð¤ì€¨©@Ñˆ¨¨µ½Ù”Ñ¡”Í¡¥À±¥™•å±”™É½´MÑ…ÉÐ€¡½¹”Í¡…É•Í¡¥À¤Ñ¼Á•Èµ©½¥¸€¡•… Á±…å•È±½…‘Ì½É•…Ñ•Ì)Ñ¡•¥È½Ý¸Í¡¥ÀìÁ•ÉÍ¥ÍÑ•¹”­•å•‰äÁ±…å•È¥¤ì€¨©@ÑŒ¨¨Í•Ð}ÕÉÉ•¹Ñ€¥¸=¹A…å±½…‘€€¬‰•™½É”Á•ÈµÁ±…å•È)MÑ…µÁM¡¥À¥¸©½¥¸½ÑÉ…Ù•°€¬Á•ÈµÁ±…å•È¥¸Ñ¡”ÍÁ…”µ½µ‰…ÐÑ¥¬ì€¨©@Ñ¨¨Õ¹Ñ…¹±”Ñ¡”Ñ•ÍÐ½ÁÕ‰±¥Œ…•ÍÍ½ÉÌ(¡Í•ÉÙ•È¹M¡¥Á€½=Ý¹•‘M¡¥ÁÍ€ƒŠH™¥ÉÍÐ©½¥¹•Á±…å•È¤€¬„ÑÝ¼µÁ±…å•È™±••Ðµ¥Í½±…Ñ¥½¸Ñ•ÍÐ¸M¡¥ÀµÍÑ…µÀÍÑ…Ñ”)ÍÑ…åÌÁ•ÈµÝ½É±€¡™¥¹”Ý¡¥±”•… ½ÕÁ¥•Ý½É±¡…Ì½¹”Á±…å•ÈìÍ¡…É•µÝ½É±µÕ±Ñ¤µÍ¡¥À¥Ì@Ü¤¸()|¡½¹”€ÈÀÈØ´ÀØ´ÀØƒŠPÍ•”Ñ¡”€‰MÑ…ÉÑ•ÈÝ•…Á½¹Ì€¬Í¡¥ÀÝ•…Á½¹Ì™¥¹¥Í¡•€¡\ÇŠM\Ô¤ˆ•¹ÑÉä…‰½Ù”¸¥|((ŒŒŒ=É‰¥Ñ…°‰½‘¥•Ì¥¸Ñ¡”Á±…¹•ÐÍ­äƒŠPƒŠr=9€ÈÀÈØ´ÀØ´ÄÄ€¡Í½Á”Á•ÈÕÍ•Èè9<ÍÑ…Ñ¥½¹Ìì½Ý¸Í­äå±•Ì¤)M¡¥ÁÁ•…ÌM­å	½‘¥•ÍY¥•Ý€€¡±¥•¹Ð…µ‰¥•¹”°Ý¥É•¥¸]½É±‘I¥œ¤èÑ¡”ÍåÍÑ•´Ì½Ñ¡•È19	1‰½‘¥•ÌƒŠP)µ½½¹Ì°¹•¥¡‰½ÕÈÁ±…¹•ÑÌ°±…¹‘…‰±”…ÍÑ•É½¥‘Ì€¡ÍÑ…Ñ¥½¹Ì•áÁ±¥¥Ñ±ä•á±Õ‘•¤ƒŠP¡…¹œ¥¸Ñ¡”ÍÕÉ™…”Í­ä…Ì)±¥ÐÑ¥¹Ñ•ÍÁ¡•É•Ì¸€¨©… ‰½‘ä™½±±½ÝÌ¥ÑÌ½Ý¸‘•Ñ•Éµ¥¹¥ÍÑ¥ŒÍ­äå±”±¥­”Ñ¡”ÍÕ¸¨¨€¡½É‰¥ÐÍÁ••…Ì„)™É…Ñ¥½¸½µÕ±Ñ¥Á±”½˜Ñ¡”±½…°‘…äƒŠP…ÍÑ•É½¥‘ÌÉ½ÍÌ™…ÍÐ°Á±…¹•ÑÌ‘É¥™ÐÍÑ…Ñ•±äƒŠPÁ±ÕÌ„Á¡…Í”€¬¥ÑÌ½Ý¸)Á…Ñ ‰•…É¥¹œ¤°¡…Í¡•™É½´ÕÉÉ•¹ÐµÁ±…¹•Ð­‰½‘ä°Í¼•Ù•ÉäÝ½É±¡…Ì„Õ¹¥ÅÕ”°ÍÑ…‰±”Í­ä¡½É•½É…Á¡ä¸)M¥é•‰äÝ½É±±…ÍÌ°Ñ¥¹Ñ•‰äÁ±…¹•ÐÑåÁ”°„Ñ½Õ ‰É¥¡Ñ•È…Ð¹¥¡Ð°¡½É¥é½¸µ™…‘•°Ñ•ÉÉ…¥¸µ½±Õ‘•ì)ÍÑ…Èµ…À…ÕÑ¼µÉ•ÅÕ•ÍÑ•…™Ñ•ÈÍÁ…Ý¸°É•‰Õ¥±Ð½¸ÑÉ…Ù•°€¡ÍÑ…±”µµ…ÀÕ…É¤¸=É¥¥¹…°Á±…¸­•ÁÐ‰•±½Üè(´M­å	½‘¥•Í€½µÁ½¹•¹ÐÑ¡…Ð°½¹±äÝ¡¥±”½¸„Á±…¹•ÐÍÕÉ™…”€¡¹½Ð¥¸ÍÁ…”€¼¹½Ð‰½…É‘•¤°Á±…•ÌÍµ…±°(€€¨©±¥ÐÍÁ¡•É”‰¥±±‰½…É‘Ì¨¨™½ÈÑ¡”ÍåÍÑ•´Ì¹•¥¡‰½ÕÉÌ…¹Íµ…±°€¨©ÍÑ…Ñ¥½¸¥½¹Ì¨¨™½ÈÍÑ…Ñ¥½¹Ì½É‰¥Ñ¥¹œ(€Ñ¡”ÕÉÉ•¹Ð‰½‘ä°¥¸Ñ¡”Í­ä‘½µ”ƒŠP‘¥É•Ñ¥½¸‘•É¥Ù•™É½´Ñ¡”ÍÑ…Èµ…ÀÌÉ•±…Ñ¥Ù”ÍåÍÑ•´½½É‘Ì(€€¡…µ”¹MÑ…É5…Á€°…±É•…‘ä±¥•¹ÐµÍ¥‘”¤°‘¥ÍÑ…¹”Í…±•Ñ¼Ñ¡”™…ÈÁ±…¹”°™½±±½Ý¥¹œÑ¡”…µ•É„±¥­”Ñ¡”(€ÍÑ…É™¥•±¸Q¥¹Ð½Í¥é”™É½´•… ‰½‘äÌÁ±…¹•ÐÑåÁ”ìÍÑ…Ñ¥½¹ÌÉ•……ÌÑ¥¹äµ•Ñ…±±¥ŒÍÁ•­Ì½É½ÍÌÍ¡…Á•Ì¸(´Y¥Í¥‰±”‘…ä€¬¹¥¡Ð€¡„Ñ½Õ ‰É¥¡Ñ•È…Ð¹¥¡Ð¤ì„Ù•ÉäÍ±½Ü‘É¥™ÐÍ¼Ñ¡•ä™••°±¥­”Ñ¡•ä½É‰¥Ð¸(´A¡…Í¥¹œè€¨©<Ä¨¨É•¹‘•È¹•¥¡‰½ÕÉÌ€¬ÍÑ…Ñ¥½¹Ì™É½´Ñ¡”ÍÑ…Èµ…Àì€¨©<È¨¨Í±½Ü½É‰¥Ñ…°‘É¥™Ðì€¨©<Ì¨¨Á•ÈµÑåÁ”(€±½½¬€¬ÍÑ…Ñ¥½¹Ìµ½˜µÑ¡¥Ìµ‰½‘äÍ¡½Ý¸¹•…É•È½‰¥•Èì€¨©<Ð¨¨½ÁÑ¥½¹…°±…‰•±Ì½¸±½½¬½Í…¸¸((ŒŒŒ½½ÉÌƒŠPƒŠrU11dÍ¡¥ÁÁ•€¡Í•ÑÑ±•µ•¹ÑÌ€¬ÍÑ…Ñ¥½¹Ì€¬Í¡¥À€¬Á±…•…‰±”ìÙ•É¥™¥•€ÈÀÈØ´ÀØ´ÄÀ¤(¨©M•ÑÑ±•µ•¹Ð‘½½ÉÌ‘½¹”¨¨€¡ÇŠMÔ¤¸€¨©MÑ…Ñ¥½¹Ì€¬Í¡¥À‘½¹”Ñ½¼¨¨€¡Ñ¡¥ÌÍ•Ñ¥½¸Ý…ÌÍÑ…±”¤èÑ¡”ÍÑ…Ñ¥½¸)•¹•É…Ñ½È•µ¥ÑÌ‘½½É}Í±¥‘•€µ…É­•ÉÌ°I•¥ÍÑ•ÉMÑ…Ñ¥½¹½½ÉÍ€É•¥ÍÑ•ÉÌÑ¡•´½¸‰½…É‘¥¹œ°Í¡¥À¡…Ñ¡•Ì…É”)•¹•Éä‘½½ÉÌ™É½´M¡¥ÁMÑ…µÁÌ¹½½ÉÍ€°…¹Ñ¡”€¨©Á±…•…‰±”‘½½È¨¨€¡É…™Ñ…‰±”‘½½É}Í±¥‘•€½‘½½É}¡¥¹•€¥Ñ•µÌ°)Á±…”½É•µ½Ù”€¬Á•ÉÍ¥ÍÑ•¹”°A±…•…‰±•½½ÉQ•ÍÑÍ€¤•á¥ÍÑÌ¸=É¥¥¹…°Á±…¸­•ÁÐ‰•±½Ü™½ÈÉ•™•É•¹”è(´€¨©M¤µ™¤Í±¥‘¥¹œ‘½½ÉÌ¨¨ƒŠP…ÕÑ¼½Á•¸½±½Í”èÑ¡”Í•ÉÙ•È½Á•¹ÌÑ¡•´Ý¡•¸„Á±…å•È¥ÌÝ¥Ñ¡¥¸É…¹”…¹(€…ÕÑ¼µ±½Í•ÌÑ¡•´…™Ñ•È„Í¡½ÉÐ‘•±…ä¸½È€¨©ÍÑ…Ñ¥½¹Ì€¬¥Ñ¥•Ì½Ñ½Ý¹Ì¨¨€¡…¹Ñ¡”€¨©Í¡¥À¨¨¤¸(´€¨©!¥¹•€‰¹½Éµ…°ˆ‘½½ÉÌ¨¨ƒŠPµ…¹Õ…°èÁÉ•ÍÌ€¨©¨¨Ñ¼Ñ½±”¸½È€¨©Ù¥±±…•Ì½¡…µ±•ÑÌ¨¨¸()½½ÉÌ…É”€¨©µ…É­•ÉÌ¨¨°¹½ÐÙ½á•°‰±½­Ì€¡„€Ë\Ì‘½½ÉÝ…ä½Á•¹¥¹œÍÑ…åÌ…¥ÈìÑ¡”‘½½È•¹Ñ¥Ñä™¥±±Ì¥Ðì¥ÑÌ)½±±¥‘•È±½Í•ÌÑ¡”…ÀÝ¡•¸Í¡ÕÐ¤¸A¡…Í•Á±…¸è(´€¨©ÄƒŠPÍ•ÉÙ•È…µ•M•ÉÙ•É½½ÉÍ€è¨¨„Á•ÈµÝ½É±M•ÉÙ•É½½Èì%°QåÁ”¡Í±¥‘”½¡¥¹”¤°A½Ì°…¥¹œ°=Á•¸°(€ÕÑ½±½Í•Q¥µ•Èõ€É•¥ÍÑÉä‰Õ¥±Ð™É½´ÍÑÉÕÑÕÉ”µ…É­•ÉÌ½¸ÍÑ…µÀìQ¥­½½ÉÍ€…ÕÑ¼µ½Á•¹ÌÍ±¥‘”‘½½ÉÌ¹•…È(€Á±…å•ÉÌ€¬…ÕÑ¼µ±½Í•Ì…™Ñ•È„‘•±…äì!…¹‘±•½½É%¹Ñ•É…Ñ€Ñ½±•Ì„¡¥¹”‘½½ÈÑ¡”Á±…å•È™…•Ìì(€‰É½…‘…ÍÐ½½É1¥ÍÑ€½½½ÉMÑ…Ñ•¡…¹•‘€Á•ÈÝ½É±€¡Ù¥„	É½…‘…ÍÑQ½]½É±‘€¤¸±•…É•¥¸(€I•Í•Ñ]½É±‘IÕ¹Ñ¥µ•MÑ…Ñ•€¸(´€¨©ÈƒŠP±¥•¹Ð½½ÉY¥•Ý€è¨¨É•¹‘•ÉÌ•… ‘½½È™É½´½½É1¥ÍÑ€ìÍ±¥‘”€ôÑÝ¼Á…¹•±ÌÑ¡…ÐÍÝ½½Í …Á…ÉÐ°(€¡¥¹”€ô„±•…˜Ñ¡…ÐÍÝ¥¹ÌøäÃ
Àì„	½á½±±¥‘•É€•¹…‰±•Ý¡¥±”ù±½Í•°‘¥Í…‰±•Ý¡¥±”½Á•¸ìÍ¤µ™¤(€ÍÝ½½Í ÙÌ¸Ý½½É•…¬M`€¡±¥•¹ÑÕ‘¥½€¤¸5¥ÉÉ½ÉÌ9ÁY¥•Ý€¸(´€¨©ÌƒŠP•¹•É…Ñ½ÉÌÁ±…”‘½½ÉÌ…Ð‘½½ÉÝ…åÌè¨¨MÑ…Ñ¥½¹•¹•É…Ñ½È¹ÕÑ½½É€ƒŠH„Í±¥‘”µ‘½½Èµ…É­•ÈìÍ•ÑÑ±•µ•¹Ð(€•¹•É…Ñ½ÈÁ¥­Ì€¨©Í±¥‘”¨¨™½È¥Ñä½Ñ½Ý¸°€¨©¡¥¹”¨¨™½ÈÙ¥±±…”½¡…µ±•ÐìMÑ…Ñ¥½¹•¹•É…Ñ½É€½Í¡¥À¡Õ±°(€‘½½ÉÝ…åÌƒŠHÍ±¥‘”¸-••ÀÑ¡”½Á•¹¥¹œ…¥ÈÍ¼Ñ¡”•¹Ñ¥ÑäÍ¡½ÝÌÑ¡É½Õ ¸(´€¨©ÐƒŠP•‘¥Ñ½ÉÌè¨¨…‘‘½½Èµ…É­•ÉÌÑ¼Ñ¡”Á…±•ÑÑ•ÌƒŠP€¨©ÍÑ…Ñ¥½¸•‘¥Ñ½È¨¨€¡Í±¥‘”¤°€¨©Í•ÑÑ±•µ•¹Ð•‘¥Ñ½È¨¨(€€¡‰½Ñ Í±¥‘”€¬¡¥¹”Í¼Ñ¡”‘•Í¥¹•ÈÁ¥­Ìì•¹•É…Ñ½ÈÍÑ¥±°…ÕÑ¼µÁ¥­Ì‰äÑ¥•È¤°€¨©Í¡¥À•‘¥Ñ½È¨¨€¡Í±¥‘”¤¸(€5…É­•ÉÌ™±½ÜÑ¡É½Õ MÑÉÕÑÕÉ•Q•µÁ±…Ñ•€•±±Ì€¡­¥¹ô‰µ…É­•Èˆ°¥ô‰‘½½É}Í±¥‘”ˆ¼‰‘½½É}¡¥¹”ˆ¤ƒŠHÄÉ•¥ÍÑÉä¸(´€¨©ÔƒŠP±½…±¥é…Ñ¥½¸€¬Ñ•ÍÑÌè¨¨•¸½‘”¹…µ•ÌìÑ•ÍÑÌè„Í±¥‘”‘½½È½Á•¹ÌÝ¡•¸„Á±…å•ÈÍÑ•ÁÌÝ¥Ñ¡¥¸É…¹”€¬(€…ÕÑ¼µ±½Í•Ìì„¡¥¹”‘½½ÈÑ½±•Ì½¸¥¹Ñ•É…ÐìÑ¡”½±±¥‘•È‰±½­ÌÁ…ÍÍ…”Ý¡¥±”±½Í•¸((ŒŒŒƒŠr½¹”€ ÈÀÈØ´ÀØ´ÀÜ¤èÍÁ•¥•Ì½™±½É„½½±½ÕÈ½¹…µ¥¹œ½Ù•É¡…Õ°)µÕ±Ñ¤µÁ¡…Í”™•…ÑÕÉ”É•ÅÕ•ÍÑ•€ÈÀÈØ´ÀØ´ÀÜƒŠPÉ…¹‘½´Á•ÈµÝ½É±™±½É„€˜™…Õ¹„ÍÁ•¥•ÌÝ¥Ñ •¹•É…Ñ•±½½­Ì€¬)¹…µ•Ì°Ý¥±‘•È½±½ÕÉÌ°Õ¹¥™½É´Á•ÈµÁ±…¹•Ð™±½É„¡Õ”°…¹™Õ±°=Á•¹$½±•Ù•¹1…‰Ì…ÍÍ•Ð½Ù•É…”¸Q¡”ÕÍ•È¡½Í”(¨¨‰Ý½É¬Ñ¡”Á±…¸¥¸½É‘•Èˆ¨¨…¹€¨©…Ù”‰±…¹­•Ð…ÁÁÉ½Ù…°™½ÈÁ…¥…ÍÍ•Ð•¹•É…Ñ¥½¸¨¨€¡=Á•¹$Ñ•áÑÕÉ•Ì€¬)±•Ù•¹1…‰ÌÍ½Õ¹‘ÌƒŠP¹¼™ÕÉÑ¡•ÈÁ•Èµ‰…Ñ ½¹™¥Éµ…Ñ¥½¸¹••‘•ì­•åÌ…É”¥¸Ñ½½±Ì½…¤µ…ÍÍ•ÑÌ¼¹•¹Ù€°ÉÕ¸Ù¥„ÕÙ€¤¸((´ƒŠr€¨©A¡…Í”€ÄƒŠPÁ•ÈµÍåÍÑ•´ÍÑ…È½±½ÕÈ¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀÜ¤ƒŠPÉ•Á±…•Ñ¡”€ÔµÍÝ…Ñ ÍÕ¸Á…±•ÑÑ”Ý¥Ñ (€MÑ…É½±½È¡ÍåÍÑ•´¥€è„Ý•¥¡Ñ•¡½ÓŠI½½°ÍÑ•±±…ÈÉ…µÀ€¡‰±Õ”µÝ¡¥Ñ—ŠIÝ¡¥Ñ—ŠIå•±±½ßŠI½É…¹—ŠIÉ•¤‰±•¹‘•‰ä„(€Í•½¹¡…Í °Í¼•Ù•ÉäÍåÍÑ•´•ÑÌ„‘¥ÍÑ¥¹ÐÍÕ¸Ñ¥¹Ð¸±É•…‘äÍ¡…É•‰•ÑÝ••¸Ñ¡”Á±…¹•ÐÍÕÉ™…”€¬ÍÁ…”€¬(€ÍÑ…Ñ¥½¸Ù¥•ÝÌÙ¥„]½É±‘¹Ù¥É½¹µ•¹Ð¹MÕ¹½±½É€ìÕ¹¥™¥•Ñ¡”•¹Øµ¹Õ±°™…±±‰…¬Ñ½¼¸€¡€Áäàá„å€¤(´ƒŠr€¨©A¡…Í”€ÈƒŠP™±½É„É”µÑ¥¹Ð•¹¥¹”¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀÜ¤ƒŠPÑ¡”Í•ÉÙ•ÈÁ¥­Ì„€¨©Á•ÈµÁ±…¹•Ð™±½É„‰…Í”¡Õ”¨¨(€€¡±½É…½±½É€èÉ••¸µ‘½µ¥¹…¹ÐÁ…±•ÑÑ”Ý¥Ñ É…É•È‰É½Ý¸½Á¥¹¬½ÁÕÉÁ±”½…µ‰•È°‘•Ñ•Éµ¥¹¥ÍÑ¥Œ™É½´Í••­Á±…¹•Ð¤°(€‰É½…‘…ÍÐÙ¥„]½É±‘¹Ù¥É½¹µ•¹Ð¹±½É…Q¥¹Ñ€¸Q¡”µ•Í¡•ÈÑ…Ì™±½É„Ù•ÉÑ¥•ÌÝ¥Ñ „™±…œ¥¸Qa==IÄ¹å€(€€¡%Í±½É…	±½­€ƒŠP™±½É…|©€½¹±äìÑÉ••Ì­••ÀÑ¡•¥È½±½ÕÉÌ¤ìM­å€™••‘ÌÑ¡”¡Õ”Ñ¼Ñ¡”‰±½¬Í¡…‘•È…ÌÑ¡”(€±½‰…°}M}±½É…Q¥¹Ñ€ì	±½­Ñ±…Ì¹Í¡…‘•É€€¨©‘•Í…ÑÕÉ…Ñ•ÌÑ¡”™±½É„Ñ¥±”Ñ¼¥ÑÌ±Õµ¥¹…¹”…¹É”µÑ¥¹ÑÌ¥Ð¨¨(€‰äÑ¡”¡Õ”¸9¼Ñ¥±”É••¸€¬¹¼É”µµ•Í ¹••‘•€¡¥ÐÌ„±¥Ù”Í¡…‘•È±½‰…°°½™˜¥¸ÍÁ…”½ÍÑ…Ñ¥½¹Ì¤¸É…åÍ…±”(€™±½É„Ñ¥±•Ì€¡A¡…Í”€Õ„¤‰•½µ”½ÁÑ¥½¹…°Á½±¥Í ¹½Ü¸Q•ÍÐè±½É…Q¥¹Ñ}%Í½±½ÕÉ™Õ±A±…¹•Ñ!Õ•€¸(´ƒŠr€¨©A¡…Í”€ÌƒŠP±½É…•¹•É…Ñ½È€¬¹…µ•Ì¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀÜ¤ƒŠP„¹•Ü±½É…•¹•É…Ñ½È¹•¹•É…Ñ•I½ÍÑ•È¡Á±…¹•Ð°(€Í••¥€‘•É¥Ù•Ì„Á•ÈµÝ½É±™±½É„É½ÍÑ•Èè•… …É¡•ÑåÁ”‰±½¬€¡±½É……Ñ…±½€¤‰•½µ•Ì„¹…µ•ÍÁ•¥•Ì(€€¡9…µ••¹•É…Ñ½È¹±½É…€¤Ý¥Ñ …¸•‘¥‰±”¼¨©Ñ½á¥Œ¨¨ÑÉ…¥Ð°‘•Ñ•Éµ¥¹¥ÍÑ¥Œ™É½´Í••­Á±…¹•Ð¸Q¡”Í•ÉÙ•Èµ…ÁÌ(€‰±½¯ŠIÍÁ•¥•Ì€¡±½É…MÁ•¥•Í½É	±½­€¤ì€¨©Í…¹¹¥¹œ„Á±…¹Ð¹½ÜÍ¡½ÝÌ¥ÑÌ½¥¹•¹…µ”€¬‘¥‰±”½Q½á¥Œ¨¨€¡Ñ¡”(€‰±½¬‰É…¹ ¥¸…µ•M•ÉÙ•ÉM…¹¹¥¹€¤¸A•ÈµÝ½É±¥‘•¹Ñ¥Ñä€ôÑ¡”A¡…Í”´È¡Õ”€¬Ñ¡”½¥¹•¹…µ•Ì€¬Ñ½á¥¥Ñä(€½Ù•ÈÑ¡”Í¡…É•…É¡•ÑåÁ”‰±½­Ì€¡¹¼‘å¹…µ¥Œ‰±½­Ì¹••‘•¤¸Q•ÍÑÌè±½É…I½ÍÑ•É}9…µ•ÍÙ•ÉåÉ¡•ÑåÁ•Š™€°(€±½É…I½ÍÑ•É}%ÍµÁÑå}=¹	…ÉÉ•¹]½É±‘€¸ƒŠr€¨©Ñ½á¥Œ™±½É„¹½Ü‰¥Ñ•Ì‰…¬¨¨€ ÈÀÈØ´ÀØ´ÀÜ¤è¡…ÉÙ•ÍÑ¥¹œ„Ñ½á¥Œ(€ÍÁ•¥•Ìå¥•±‘Ì€¨©Ñ½á¥}‰•ÉÉ¥•Í€¨¨€¡„¡…Éµ™Õ°½¹ÍÕµ…‰±”°½¹ÍÕµ•!•…±Ñ €´Äá€¤¥¹ÍÑ•…½˜•‘¥‰±”‰•ÉÉ¥•ÌƒŠP(€	É•…­	±½­Ñ€É•µ…ÁÌÑ¡”‘É½À‰äÑ¡”Ý½É±Ì™±½É„ÍÁ•¥•Ì°Í¼Ñ¡”Í…¸Ì‘¥‰±”½Q½á¥ŒÝ…É¹¥¹œ¡…ÌÑ••Ñ ¸(€Q•ÍÐèQ½á¥±½É…}e¥•±‘ÍQ½á¥	•ÉÉ¥•ÍŠ™€¸ƒŠr€¨©Á•ÈµÝ½É±…É¡•ÑåÁ”ÍÕ‰Í•ÑÑ¥¹œ¨¨€ ÈÀÈØ´ÀØ´ÀÜ¤è•… Ý½É±¹½Ü(€…Ñ¥Ù…Ñ•Ì½¹±äøØÀ”½˜Ñ¡”™±½É„™½ÉµÌ€¡±½É…MÁ•¥•Ì¹Ñ¥Ù•€¤°Í¼Ý½É±‘Ì‘¥™™•È¥¸Á±…¹Ð€©Í¡…Á•Ì¨°¹½Ð©ÕÍÐ(€¡Õ”½¹…µ•ÌƒŠPÝ¥Ñ …¸¹ÍÕÉ•½Ù•É…•€Á…ÍÌÑ¡…Ð™½É”µ…Ñ¥Ù…Ñ•ÌÑ¡”µ¥¹¥µÕ´Í¼•Ù•Éä±…¹¡½ÍÐÍÕÉ™…”€¬Ñ¡”(€Í•…Ì­••ÀƒŠ&”Ä…Ñ¥Ù”ÍÁ•¥•Ì€¡¹¼‰…É”‰¥½µ”¤¸]½É±‘•¹•É…Ñ½È¹I•Í½±Ù•±½É…€‰Õ¥±‘ÌÑ¡”Á•ÈµÍÕÉ™…”Á½½±Ì€¬(€­•±À½±¥±ä…Ñ¥¹œ™É½´Ñ¡”…Ñ¥Ù”ÍÕ‰Í•ÐìÁ±…•µ•¹Ð€¬Í…¸Í¡…É”}µ•Ñ„¹M••‘€Í¼Ñ¡•ä…±Ý…åÌ…É•”¸Q•ÍÐè(€±½É…I½ÍÑ•É}Ñ¥Ù…Ñ•ÍMÕ‰Í•Ñ}	ÕÑ-••ÁÍÕ±±½Ù•É…•€¸(´ƒŠ^D€¨©A¡…Í”€ÐƒŠP™…Õ¹„Á½±¥Í €¬¹…µ•Ì¨¨ƒŠPƒŠr€¨©¹…µ•Ì€¬½±½ÕÉÌ‘½¹”¨¨€¡€ÍŒÄÔÀÁ€¤è„Í¡…É•9…µ••¹•É…Ñ½É€(€½¥¹ÌÁ•ÈµÍÁ•¥•Ì¹…µ•Ì€¡Í¡½Ý¸½¸Í…¸…ÌÑ¡”É•…‘½ÕÐÍÕ‰©•ÐìÉ¥‘•Ì9•ÑÉ•…ÑÕÉ”¹9…µ•€¤ìA¥­½±½É€¹½Ü(€µ…­•Ìù¡…±˜½˜ÍÁ•¥•ÌÙ¥Ù¥•á½Ñ¥Ì€¡!MX¡Õ•ÌƒŠHÁ¥¹¬½Ù¥½±•Ð½å•±±½Ü½Ñ•…°¤¸ƒŠ>Ì€¨©ÍÑ¥±°Ñ¼‘¼è¨¨Á•È´¨©‰¥½µ”¨¨(€ÍÁ•¥•Ì…™™¥¹¥Ñä€¡ÍÁ…Ý¸ÍÁ•¥•Ì‰äÑ¡”Á±…å•ÈÌÕÉÉ•¹Ð‰¥½µ”°¹½Ð©ÕÍÐÁ•ÈµÁ±…¹•Ð¤¸ƒŠr€¨©‘½¹”€ÈÀÈØ´ÀØ´ÀÜ¨¨è(€É•…ÑÕÉ•MÁ•¥•Ì¹	¥½µ•™™¥¹¥Ñå€€¡…ÍÍ¥¹•Á•ÈÍÁ•¥•Ì™É½´Ñ¡”Á±…¹•ÐÌ‰¥½µ”½Õ¹Ðì€´Ä½¸Í¥¹±”µ‰¥½µ”(€Ý½É±‘Ì¤ìQÉåMÁ…Ý¹É•…ÑÕÉ•9•…É€¹½Ü‘½•Ì„‰¥½µ”µ™¥ÉÍÐÑÝ¼µÁ…ÍÌÍÁ…Ý¸€¡¹…Ñ¥Ù”ÍÁ•¥•ÌÁÉ•™•ÉÉ•°…¹ä…Ì(€™…±±‰…¬¤Í¼„µÕ±Ñ¤µ‰¥½µ”Ý½É±Í¡½ÝÌ‘¥™™•É•¹Ð™…Õ¹„Á•ÈÉ•¥½¸¸Q•ÍÐè(€I½ÍÑ•É}ÍÍ¥¹Í	¥½µ•™™¥¹¥Ñå}MÁÉ•…‘É½ÍÍ5Õ±Ñ¥	¥½µ•]½É±‘€¸(´ƒŠ^D€¨©A¡…Í”€ÔƒŠP…ÍÍ•Ð•¹•É…Ñ¥½¸€¡Á…¥°…ÁÁÉ½Ù•¤¨¨ƒŠPƒŠr€¨¨¡„¤™±½É„Ñ•áÑÕÉ•Ì½µÁ±•Ñ”¨¨è•¹•É…Ñ•€¬(€‰Õ¹‘±•™±½É…}­•±Á€€¬™±½É…}±¥±å€€¡=Á•¹$¤°Í¼€¨©…±°€ÄÔ™±½É…|©€‰±½­Ì¹½Ü¡…Ù”Ñ¥±•Ì¨¨€¡…Õ‘¥Ð½¹™¥Éµ•(€™±½É„€ÄÔ¼ÄÔ€¬É•…ÑÕÉ”¡¥‘•Ì€ÄÈ¼ÄÈ½Ù•É•ì½¹±äÍÁ•¥…°‰±½­ÌƒŠP±¥¡ÑÌ½™½É”µ™¥•±½±…‘‘•È½ÍÑ…¥ÉÌ½‘…Ñ„µ…¡”(€ƒŠPÕÍ”	…Í•½±½È°‰ä‘•Í¥¸¤¸É…åÍ…±”É••¸¥ÌÕ¹¹••ÍÍ…ÉäƒŠPÑ¡”A¡…Í”´ÈÍ¡…‘•ÈÉ”µÑ¥¹ÑÌ‰ä±Õµ¥¹…¹”°Í¼(€Ñ¡”•á¥ÍÑ¥¹œ½±½ÕÈÑ¥±•Ì…±É•…‘äÝ½É¬¸ƒŠr€¨¨¡Œ¤µ½É”É•…ÑÕÉ”…±±Ì¨¨è•¹•É…Ñ•€Ø¹•Ü±•Ù•¹1…‰ÌÍ¥¹…ÑÕÉ”(€…±±Ì€¡ÑÉ¥±°€¼±¥¬€¼ÉÕµ‰±”€¼‰•±±½Ü€¼¡¥ÍÌ€¼¡¥ÑÑ•È¤ƒŠHÉ•…ÑÕÉ•Y¥•Ü¹…±±Ímu€¹½Ü¡…Ì€¨¨ÄÈ¨¨€¡…Õ‘¥¼(€€ÄÀØƒŠH€ÄÄÈ¤°Í¼„Ý½É±Ì™…Õ¹„Í½Õ¹‘Ì™…Èµ½É”Ù…É¥•¸ƒŠ>Ì€¨©ÍÑ¥±°Ñ¼‘¼è¨¨€¡ˆ¤µ½É”É•…ÑÕÉ”¡¥‘•Ì½¹±ä¥˜(€¹•Ü‰½‘äÁ…ÉÑÌ…É”…‘‘•¸()±Í¼¥¸Ñ¡”‰…­±½œè€¨©µÕ±Ñ¥Á±…å•ÈÁ±…å•Èµ¹…µ”É•Í•ÉÙ…Ñ¥½¸¨¨ƒŠPƒŠr=9€¡Í•ÉÙ•ÈµÍ¥‘”¹…µ”Ù•É¥™¥…Ñ¥½¸ìÍ•”Ñ¡”(‰A±…¹¹•ƒŠPÉ•ÅÕ•ÍÑ•€ÈÀÈØ´ÀØ´ÀØˆ•¹ÑÉä‰•±½Ü¤¸()Ù•ÉåÑ¡¥¹œ‰Õ¥±‘Ì€¬€¨¨ÌÀÈÑ•ÍÑÌÁ…ÍÌ¨¨…Ì½˜€Áäàá„å€¸((ŒŒŒA±…¹¹•ƒŠPÉ•ÅÕ•ÍÑ•€ÈÀÈØ´ÀØ´ÀØ€¡›ñÈÍÃ‘Ñ•È¤(´ƒŠr€¨©5¥¹•…‰±”Ý…Ñ•È½±…Ù„€¡Ý¥Ñ Ñ¡”µ¥¹¥¹œ‰•…´¤€¬Í½ÕÉ”±½¥Œ¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀØ¤ƒŠPÝ…Ñ•È½±…Ù„…É”¹½Ü(€µ¥¹•…‰±•€‰ÕÐÉ•ÅÕ¥É•‘Q½½°è‘É¥±±€€¬µ¥¹Q½½±Q¥•Èè€Í€°Í¼€¨©½¹±äÑ¡”µ¥¹¥¹œ‰•…´¨¨±•…ÉÌÑ¡•´€¡Ñ¡”(€‰…Í¥Œ½Ñ¥Ñ…¹¥Õ´‘É¥±±Ì…¸Ð¤ì•… ‘É½ÁÌ¥ÑÌÁ±…•…‰±”¥Ñ•´¸I•µ½Ù¥¹œ„™±Õ¥•±°…±±Ì=¹±Õ¥‘I•µ½Ù•‘€°(€Ý¡¥ €¨©Ý…­•ÌÑ¡”ÍÕÉÉ½Õ¹‘¥¹œ™±Õ¥¨¨Í¼„‰½‘äÉ•™¥±±ÌÑ¡”¡½±”ƒŠPÝ½É±‘•¸Í•„•±±Ì…Ð…Ì™Õ±°Í½ÕÉ•Ì°(€Í¼å½Ô…¸½¹±ä‘É…¥¸„€¨©™¥¹¥Ñ”Á½½°¨¨‰äÑ…­¥¹œ¥ÑÌ±…ÍÐ™••‘¥¹œ•±±Ì¸Í•ÑÑ±”Õ…É¥¸Q¥­±Õ¥‘Í€(€€¡!…Í¥É9•¥¡‰½É€¤±•ÑÌ…±´™Õ±°•±±Ì¼‘½Éµ…¹Ð°Í¼„‰¥œÍ•„‘½•Í¸Ð­••À•Ù•Éä•±°…Ñ¥Ù”¸Q•ÍÑÌè(€]…Ñ•É¹‘1…Ù…}É•5¥¹•…‰±•}=¹±å	åQ¡•5¥¹¥¹	•…µ€°]…Ñ•É}	…Í¥É¥±±…¹¹½Ñ}5¥¹¥¹	•…µ…¹€°(€]…Ñ•É	½‘å}I•™¥±±Í5¥¹•‘!½±•€¸(´ƒŠr€¨©M½ÕÉ”½™±½Ý¥¹œ™±Õ¥‘ÌƒŠP¹¼™±½…Ñ¥¹œÍ¡•±˜€¬É•ÑÉ…Ñ¥½¸¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÄÐ¤ƒŠP™¥á•„É•Á½ÉÑ•‰ÕœÝ¡•É”(€Ý…Ñ•ÈÁ½ÕÉ•™É½´„µ½Õ¹Ñ…¥¸Á½¹½Ù•È„±¥™˜€¨©¡Õ¹œ¥¸Ñ¡”…¥È½Ù•ÈÑ¡”Á±…¥¸¨¨¥¹ÍÑ•…½˜™…±±¥¹œ¸I½½Ð(€…ÕÍ”è„±¥À•±°°½¹”¥Ð¡…‘É½ÁÁ•½¹”‰±½¬°Ñ½½¬Ñ¡”Í¥‘•Ý…åÌµÍÁÉ•…‰É…¹ …¹É…Ý±•…±½¹œ…Ð¥ÑÌ(€½Ý¸€¡¡¥ ¤•±•Ù…Ñ¥½¸½Ù•ÈÑ¡”Ù½¥°…¹™±½Ý¥¹œÝ…Ñ•È¹•Ù•ÈÉ••‘•¸¥à¥¸…µ•M•ÉÙ•É±Õ¥‘Í€è€ Ä¤„(€€¨©…±±¥¹±Õ¥‘€Í•Ð¨¨µ…É­Ì•±±Ì™¥±±•™É½´…‰½Ù”ƒŠP„•±°™••‘¥¹œ„Ý…Ñ•É™…±°¹¼±½¹•ÈÍÁÉ•…‘ÌÍ¥‘•Ý…åÌ°(€Í¼Ý…Ñ•ÈÉ•ÍÑÌÑ¡”É¥´‰ä½¹”½Ù•É¡…¹œ•±°…¹Á½ÕÉÌÍÑÉ…¥¡Ð‘½Ý¸ì€ È¤„€¨©Í½ÕÉ”ÙÌ™±½Ý¥¹œ¨¨ÍÁ±¥ÐƒŠP„(€Í½ÕÉ”¥Ì…¸€©Õ¹ÑÉ…­•¨•±°€¡‰½ÑÑ½µ±•ÍÌ°”¹œ¸Ý½É±‘•¸Í•…Ì€¬Á±…•Ý…Ñ•È½±…Ù„¤°„™±½Ý¥¹œ•±°±¥Ù•Ì¥¸(€}™±Õ¥‘1•Ù•±€…¹•… Ñ¥¬É•½µÁÕÑ•Ì¥ÑÌMÕÁÁ½ÉÑ•‘1•Ù•±€€¡™Õ±°¥˜™±Õ¥…‰½Ù”°•±Í”‰•ÍÐ¡½É¥é½¹Ñ…°(€¹•¥¡‰½ÕÈƒŠ"H€Ä¤ìÝ¥Ñ ¹¼™••¥Ð€¨©É•ÑÉ…ÑÌ¨¨€¡‘É¥•ÌÕÀ¤…¹Ý…­•Ì¥ÑÌ¹•¥¡‰½ÕÉÌÍ¼Ñ¡”Ý¡½±”½ÉÁ¡…¹•Ñ…¥°(€É••‘•Ì¸]…­•€¹¼±½¹•ÈÁÉ½µ½Ñ•ÌÕ¹ÑÉ…­••±±Ì€¡Ñ¡•äÍÑ…äÍ½ÕÉ•Ì¤¸Q•ÍÑÌè(€]…Ñ•É}A½ÕÉ¥¹=Ù•É±¥™™}½•Í9½Ñ!…¹%¹Q¡•¥É€°±½Ý¥¹]…Ñ•É}I••‘•Í}]¡•¹%ÑÍM½ÕÉ•%ÍI•µ½Ù•‘€¸€¨¡9½Ñ”èÝ½É±‘•¸(€Á½¹‘Ì…É”ÍÑ¥±°¥¹™¥¹¥Ñ”Í½ÕÉ•ÌƒŠP™¥¹¥Ñ”ÍÁÉ¥¹Ì…É”„Í•Á…É…Ñ”™ÕÑÕÉ”¥Ñ•´¸¤¨(´€¨©5Õ±Ñ¥Á±…å•ÈÁ±…å•Èµ¹…µ”É•Í•ÉÙ…Ñ¥½¸ƒŠPƒŠr=9¸¨¨A±…å•È¹…µ•Ì…É”¹½ÜÙ•É¥™¥•½É•Í•ÉÙ•Í•ÉÙ•ÈµÍ¥‘”Í¼ÑÝ¼(€±¥•¹ÑÌ…¸Ð½±±¥‘”½¸Ñ¡”Í…µ”¹…µ”½¥‘•¹Ñ¥Ñä€¡…µ•M•ÉÙ•È¹Í€©½¥¸Á…Ñ ì½Ù•É•‰ä9…µ•Y•É¥™¥…Ñ¥½¹Q•ÍÑÍ€¤¸(€I•ÅÕ•ÍÑ•€ÈÀÈØ´ÀØ´ÀØ¸(´ƒŠr€¨©É•…ÑÕÉ”ÍÝ¥´Õ¹‘Õ±…Ñ¥½¸€¬‘¥Ù”¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀØ¤ƒŠPÉ•…ÑÕÉ•	Õ¥±‘•É€¹½Ü¡…¹Ì•Ù•ÉäÁ…ÉÐ½™˜„(€	½‘åI¥€Á¥Ù½Ðì™½È…ÅÕ…Ñ¥ŒÍÁ•¥•Ì€¡!…‰¥Ñ…Ð€ôô€‰]…Ñ•È‰€¤É•…ÑÕÉ•¹¥µ…Ñ½É€Õ¹‘Õ±…Ñ•ÌÑ¡…ÐÉ¥œ€¡„å…Ü(€Ý•…Ù”Ñ¡…Ð±…ÌÑ¡”Ñ…¥°‰•…Ð€¬„½Õ¹Ñ•ÈµÉ½±°€¬„Í±½ÜÙ•ÉÑ¥…°±¥‘”¤°‰•…ÑÌÑ¡”Ñ…¥°™…ÍÑ•È½Ý¥‘•È°…¹(€‘É½ÁÌÑ¡”±•ÌÑ¼„™¥¸µ™±ÕÑÑ•È¥¹ÍÑ•…½˜„ÍÑÉ¥‘”¸M•ÉÙ•È€¨©‘¥Ù”¨¨è‘©ÕÍÑ!…‰¥Ñ…Ñ!•¥¡Ñ€Á½ÉÁ½¥Í•Ì(€ÍÝ¥µµ•ÉÌÕÀ…¹‘½Ý¸Ñ¡”Ý…Ñ•È½±Õµ¸½Ù•ÈÑ¥µ”€¡Í¥¸¡}É•…ÑÕÉ•±½¯
ÜÀ¸ÈÈ€¬Á½Ì¥€¤°±…µÁ•Ñ¼Ñ¡”½±Õµ¸°(€¥¹ÍÑ•…½˜¡½±‘¥¹œ½¹”‘•ÁÑ ¸€¡…É±¥•ÈèÑ•ÉÉ…¥¸½Ý…Ñ•Èµ™½±±½Ý¥¹œdƒŠP±…¹½±…Ù„Ý…±¬°™±¥•ÉÌ¡½Ù•È¸¤(´ƒŠr€¨©U¹‘•ÉÝ…Ñ•ÈÍ½Õ¹™¥±Ñ•È¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀØ¤ƒŠP±¥•¹ÑÕ‘¥½€…‘‘Ì…¸Õ‘¥½1½ÝA…ÍÍ¥±Ñ•É€Ñ¼¥ÑÌ½Ý¸(€…µ•=‰©•Ð€¡Ý¡¥ …±Í¼¡½ÍÑÌ±¥•¹Ñ5ÕÍ¥€¤°Í¼Ý¡•¸Ñ¡”Á±…å•ÈÌ€¨©¡•…Í¥ÑÌ¥¹Í¥‘”„Ý…Ñ•È½±…Ù„‰±½¬¨¨(€€¡!•…‘%¹±Õ¥‘€èÍ…µÁ±”Ñ¡”‰±½¬øÄ¸Ô…‰½Ù”Ñ¡”Á±…å•ÈÉ½½Ð¤Ñ¡”ÕÑ½™˜ÍÝ••ÁÌÑ¼øØàÀ!è…¹Ñ¡”Ý¡½±”(€‰•€¬µÕÍ¥ŒµÕ™™±”ì¥ÐÍÝ••ÁÌ‰…¬Ñ¼½Á•¸…‰½Ù”Ý…Ñ•È¸€Í½¹”µÍ¡½ÑÌ€¡Ñ€¤•Ð„Á•ÈµÍ½ÕÉ”±½ÜµÁ…ÍÌÝ¡¥±”(€ÍÕ‰µ•É•Ñ½¼¸±½ÜµÁ…ÍÌ½¸Ñ¡”Õ‘¥½1¥ÍÑ•¹•È‘½•Ì¹½Ñ¡¥¹œ¥¸U¹¥ÑäƒŠP¥ÐµÕÍÐ±¥Ù”½¸Ñ¡”Í½ÕÉ•Ì°Ý¡¥ (€¥ÌÝ¡äÑ¡¥ÌÉ¥‘•ÌÑ¡”±¥•¹ÑÕ‘¥¼½‰©•ÐÉ…Ñ¡•ÈÑ¡…¸Ñ¡”…µ•É„¸(´ƒŠr€¨©ÅÕ…Ñ¥Œ™…Õ¹„€¬Ý…Ñ•È™±½É„¨¨€¡‘½¹”€ÈÀÈØ´ÀØ´ÀÜ¤ƒŠP€¨©]…Ñ•È™±½É„è¨¨ÑÝ¼¹•Ü™±½É„‰±½­Ì™±½É…}­•±Á€(€€¡ÍÑ…±¬É½½Ñ•½¸Ñ¡”Í•…‰•°É½ÝÌ„™•Ü•±±ÌÕÀ°Ñ½ÀÍÑ…åÌ½Á•¸Ý…Ñ•È¤€¬™±½É…}±¥±å€€¡Á…½¸Ñ¡”ÍÕÉ™…”(€Ý…Ñ•È•±°¤ì„MÑ…µÁ]…Ñ•É±½É…€Ý½É±‘•¸Á…ÍÌÍ••‘ÌÑ¡•´¥¸ÍÕ‰µ•É•½±Õµ¹Ì½˜€¨©Ý…Ñ•È¨¨Í•…Ì€¡¹•Ù•È(€±…Ù„¤¸	½Ñ ¥¸±½É……Ñ…±½€Ý¥Ñ Í•…‰•½Ý…Ñ•È¡½ÍÑÌ€¬…¸ÅÕ…Ñ¥€™±…œÑ¡…Ð­••ÁÌÑ¡•´½ÕÐ½˜Ñ¡”(€±…¹ÍÕÉ™…”µ™±½É„Á½½°€¡Ñ¡•¥È¡½ÍÑÌ½Ù•É±…À‘Éäµ±…¹‰±½­Ì¤¸5¥¹¥¹œÕ¹‘•ÉÝ…Ñ•È¹½ÜÉ•™¥±±ÌÑ¡”¡½±”(€€¡	É•…­	±½­Ñ€Ý…­•Ì…‘©…•¹Ð™±Õ¥Ù¥„!…Í±Õ¥‘9•¥¡‰½É€¤°Í¼¡…ÉÙ•ÍÑ¥¹œ­•±À‘½•Í¸Ð±•…Ù”…¥ÈÁ½­•ÑÌ¸(€	…Í•½±½È™…±±‰…­Ì€¡Í•„µÉ••¸¤Õ¹Ñ¥°=Á•¹$Ñ¥±•Ì…É”•¹•É…Ñ•¸Q•ÍÑÌè(€Ñµ½ÍÁ¡•É•]½É±‘}É½ÝÍÅÕ…Ñ¥±½É…€°¥É±•ÍÍ±½É…]½É±‘}É½ÝÍ9½ÅÕ…Ñ¥±½É…€¸€¨©ÅÕ…Ñ¥Œ™…Õ¹„¨¨…±É•…‘ä(€Ý½É­•€¡Ý…Ñ•Èµ¡…‰¥Ñ…ÐÍÁ•¥•Ì•¹•É…Ñ”½¸Ý…Ñ•Èµ±¥™”Á±…¹•ÑÌ€¬ÍÁ…Ý¸¥¸Ý…Ñ•È½±Õµ¹Ì€¬ÍÝ¥´½‘¥Ù”¤¸(€€©½±±½ÜµÕÀè•¹•É…Ñ”­•±À½±¥±äÑ•áÑÕÉ•Ì€¡=Á•¹$¤™½ÈÁ…É¥ÑäÝ¥Ñ Ñ¡”½Ñ¡•È™±½É„Ñ¥±•Ì¸¨(´€¨©±½É„É”µÑ¥¹ÐÁ•ÈÍÁ•¥•Ìƒ\Á±…¹•ÐƒŠPƒŠrM!%AA€ÈÀÈØ´ÀØ´ÄÄ€ ÐØÌÑ•ÍÑÌÉ••¸°±¥•¹Ð‰Õ¥±Ð¤¸¨¨(€Ù•Éä™±½É„ÍÁ•¥•Ì€¡™±½É…|¨€¬ÑÉ••}±•…Ù•Ì¤¹½ÜÉ½±±Ì=9‘•Ñ•Éµ¥¹¥ÍÑ¥Œ½±½ÕÈÁ•ÈÝ½É±ƒŠPÕ¹¥™½É´(€Ý¥Ñ¡¥¸Ñ¡”Ý½É±°‘¥™™•É•¹Ð½¸Ñ¡”¹•áÐƒŠP¥¹ÍÑ•…½˜Ñ¡”Í¥¹±”Á•ÈµÁ±…¹•Ð¡Õ”¸9¼Ñ•áÑÕÉ”É••¹•É…Ñ¥½¸(€¹••‘•èÑ¡”Í¡…‘•È…±É•…‘ä‘•Í…ÑÕÉ…Ñ•Ì™±½É„Ñ¼±Õµ¥¹…¹”‰•™½É”Ñ¥¹Ñ¥¹œ°Í¼Ñ¡”•á¥ÍÑ¥¹œÑ¥±•ÌÑ¥¹Ð(€±•…¹±ä¸%µÁ±•µ•¹Ñ…Ñ¥½¸èÍ¡…É•±½É…Q¥¹ÑÌ¹½È¡Í••°±½…Ñ¥½¸°‰±½­-•ä¥€€¡9X€¬!MX‰…¹°ÁÕÉ”(€™Õ¹Ñ¥½¸ƒŠH…±°±¥•¹ÑÌ…É•”Ý¥Ñ é•É¼ÑÉ…™™¥Œ¤ì¡Õ¹­5•Í¡•É€Qa==IÈÉ•Ü™É½´™±½…ÐÈÑ¼™±½…ÐÐ(€€¡à€ô±•…˜™±…œ…Ì‰•™½É”°€¨©åéÜ€ôÁ•ÈµÍÁ•¥•ÌÑ¥¹Ð¨¨ì‰±…¬€ô€‰¹¼Á•ÈµÙ•ÉÑ•àÑ¥¹Ðˆ¤ì‰½Ñ 	±½­Ñ±…Í€(€MÕ‰M¡…‘•ÉÌ€¡UI@€¬	Õ¥±Ðµ¥¸¤ÁÉ•™•ÈÑ¡”Ù•ÉÑ•àÑ¥¹Ð…¹™…±°‰…¬Ñ¼Ñ¡”±½‰…°}M}±½É…Q¥¹Ñ€¡Õ”ƒŠP(€Í¼µ•Í¡•Ì‰Õ¥±Ð]%Q!=UP„É•Í½±Ù•È€¡Í¡¥ÀÁÉ•Ù¥•Ü•ÑŒ¸¤­••ÀÑ¡”½±‰•¡…Ù¥½ÕÈ°…¹Ñ¡”±½‰…°…±Á¡„(€ÍÑ…åÌÑ¡”µ…ÍÑ•È•¹…‰±”€¡½™˜¥¸ÍÁ…”½µ•¹ÕÌ¤¸…µ•	½½ÑÍÑÉ…Á€É•‰Õ¥±‘ÌÑ¡”Ñ¥¹Ðµ…À½¸©½¥¸€¬Ý½É±(€¡…¹”¸Q•ÍÑÌè±½É…Q¥¹ÑQ•ÍÑÍ€€¡‘•Ñ•Éµ¥¹¥Í´°Á•ÈµÍÁ•¥•ÌÙ…É¥…¹”°Á•ÈµÝ½É±Ù…É¥…¹”°½±½ÕÈ‰…¹¤¸(€€¨¡A•Èµ	%=5Ù…É¥…Ñ¥½¸Ý¥Ñ¡¥¸½¹”Ý½É±ÍÑ…åÌ½ÁÑ¥½¹…°™ÕÑÕÉ”Á½±¥Í ¸¤¨((ŒŒŒ9½ÐÍÑ…ÉÑ•€¼±…É•È™ÕÑÕÉ”Ý½É¬ƒŠPƒŠr1=M€¡É•Í½±Ù•€ÈÀÈØ´ÀØ´Ää¤(¨¡A•ÈÕÍ•È‘•¥Í¥½¸€ÈÀÈØ´ÀØ´ÄäèÑ¡¥ÌÝ¡½±”Í•Ñ¥½¸¥Ì½¹Í¥‘•É•É•Í½±Ù•ƒŠPÑ¡”±•™Ñ½Ù•ÉÌ‰•±½Ü…É”•¥Ñ¡•È)Í¡¥ÁÁ•°ÍÕÁ•ÉÍ•‘•‰ä±…Ñ•ÈÝ½É¬°½È‘•±¥‰•É…Ñ”¹½¸µ½…±Ì¸-•ÁÐ™½ÈÑ¡”É•½É¸¤¨(´€¨©]½É±ÝÉ…À€¡Ý…±¬…É½Õ¹Ñ¡”Á±…¹•Ð¤¨¨ƒŠPƒŠr€¨©\ÃŠM\ÐÍ¡¥ÁÁ•¨¨è`¥Ì„ÝÉ…ÁÁ¥¹œ±½¹¥ÑÕ‘”€¡å±¥¹‘•È(€Ý½É±¤°Í¼å½Ô…¸Ý…±¬•…ÍÐ…¹…ÉÉ¥Ù”‰…¬…ÐÑ¡”ÍÑ…ÉÐÝ¥Ñ „€¨©Í•…´µ™É•”¨¨•‘”€¡Ñ•ÉÉ…¥¸½‰¥½µ•Ì½…Ù•Ì¼(€½É”½ÍÑÉÕÑÕÉ•Ì½¹Ñ¥¹Õ½ÕÌ…É½ÍÌ`€ô€ÀƒŠ&„`€ô€ØÀÀÀ¤¸M•…´µ™É•”•¹•É…Ñ¥½¸Ù¥„¥ÉÕ±…Èµ‘½µ…¥¸¹½¥Í”ìÍ•ÉÙ•È(€€¬±¥•¹Ð€¬Á•ÉÍ¥ÍÑ•¹”€¬¥¹Ñ•É…Ñ¥½¸…±°É½ÕÑ”`Ñ¡É½Õ ½¹”ÝÉ…À¡•±Á•È¸€¨©I•µ…¥¹¥¹œè\Ô€¡Á½±•Ì¤¨¨ƒŠP‰½Õ¹(€±…Ñ¥ÑÕ‘”€¡h¤Ý¥Ñ …¸¥”µÝ…±°½‰…ÉÉ¥•È‰¥½µ”¸Õ±°Á±…¸€¬ÁÉ½É•ÍÌ¥¸(€m‘½Ì½‘•Ù•±½Á•È½]=I1}]I@¹µ‘t¡‘½Ì½‘•Ù•±½Á•È½]=I1}]I@¹µ¤¸(´€¨©‘Ù…¹•É…Á¡¥ÌÉ½…‘µ…À¨¨ƒŠP	Õ¥±Ðµ¥¸I@ÙÌUI@‘•¥Í¥½¸°½É…åÌ°É•™±•Ñ¥½¸ÁÉ½‰•Ì°1UPÉ…‘”¸(€Õ±°É•Í•…É ¥¸m‘½Ì½‘•Ù•±½Á•È½Y9}IA!%L¹µ‘t¡‘½Ì½‘•Ù•±½Á•È½Y9}IA!%L¹µ¤¸(´€¨©Q•áÑÕÉ”…Õ‘¥Ð¨¨ƒŠPÉ•Ù¥•Ü½•áÁ…¹¥Ñ•´€˜¥½¸…ÉÐ…¹É•…ÑÕÉ”½9AÑ•áÑÕÉ”Ù…É¥•Ñä¸(´€¨©ÕU$Ñ¡•µ”Á½±¥Í ƒŠPƒŠr¥½¸Á…ÍÌM!%AA€ÈÀÈØ´ÀØ´ÄÄè¨¨€ÄÀ¹•Ü•¹•É…Ñ•å…¸±¥¹”¥½¹Ì(€€¡µ…Á}Á±…å•È½Í¡¥À½Ý…åÁ½¥¹Ð½‰•…½¸½Á…½Í•ÑÑ±•µ•¹Ð½ÉÕ¥¸½ÝÉ•¬½ÍÑ…Ñ¥½¹€€¬¥½¹}Ù•…€¤¸Q¡”Ý½É±µ…ÀÌ(€Õ¹¥½‘”µ±åÁ µ…É­•ÉÌ…É”¹½ÜMAI%QL€¡Á±…å•È…ÉÉ½ÜÉ½Ñ…Ñ•Ì°Á…‘ÌÁ¥¸Ñ¼Ñ¡”•‘”…Ì¥½¹Ì°A=$ÑåÁ•Ì(€•Ð‘¥ÍÑ¥¹Ð…ÉÐ¤Ý¥Ñ ±åÁ ™…±±‰…¬Ý¡•¸…¸¥½¸¥Ìµ¥ÍÍ¥¹œìÑ¡”±åÁ ±••¹±¥¹”‰•…µ”„É•…°(€¥½¸±••¹É½ÜìÑ¡”Y½µÁ…¹¥½¸Á…¹•°Í¡½ÝÌ¡•È…Ù…Ñ…È¡¥À‰•Í¥‘”Ñ¡”¹…µ”¸I•µ…¥¹¥¹œ€¡­•ÁÐ¤è(€¡½Ù•È½Í•±•Ñ•ÍÑ…Ñ•Ì€¬ÍÁ…¥¹œ¡…Éµ½¹¥Í…Ñ¥½¸…É½ÍÌÑ¡”¹•Ý•ÈÍÉ••¹Ì¸(´€¨©•™•ÉÉ•‰ä‘•Í¥¸¨¨€¡Í•”m‘½Ì½‘•Ù•±½Á•È½MA}=5	Q}=9AP¹µ‘t¡‘½Ì½‘•Ù•±½Á•È½MA}=5	Q}=9AP¹µ¤¤èAÙ@Í¡¥À(€½µ‰…Ð°±…É”ÉÕ¥Í•ÉÌ½‰½ÍÍ•Ì¸€¡A•ÈµÁ±…å•ÈÍ¡¥ÁÌÍ¡¥ÁÁ•¥¸@Ð¸¤((´´´((ŒŒI•™•É•¹”‘½Ì€¡½µµ¥ÑÑ•°Õ¹‘•È‘½Ì¼¤)½¹•ÁÐ½‘•Í¥¸‘•Ñ…¥°™½ÈÑ¡”±…É•ÈÍåÍÑ•µÌèMQQ%=9}M}1=Q%=9}A19€°]=I1}]IA}A19€°5U1Q%]=I1}9}MeMQ5}1%!Q}A19€°MA}=5	Q}=9AQ€°)1%9Q}=5A1Q%=9}A19€°IQ%9}Q!}M!%A}U%}A19€°MQQ%=9}MQQ159Q}%Q=I}A19€°)M!%A}QeA}%Q=I}A19€°Y9}IA!%M}A19€°M=U9}M%9€°M1}!=MQ%9€°%}5%MM%=9}	-9€°)1%9Q}M!11}9}MMQM€¸(