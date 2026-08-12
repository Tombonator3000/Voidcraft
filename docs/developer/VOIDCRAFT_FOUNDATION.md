# Voidcraft foundation

Status: first vertical-slice foundation implemented on 2026-08-07. Live Done/Open status remains in
[../../TODO.md](../../TODO.md).

## Pitch

**Voidcraft is a block-built space survival game about crossing procedural worlds and discovering that the
worlds were built as locks.** Players mine, craft, build and fly through the existing sandbox while a slow
cosmic-horror campaign turns familiar planets into evidence of something older than their stars.

The project is built on the AGPL-licensed
[Blocks Beyond the Stars](https://github.com/marceld23/BlocksBeyondTheStars) codebase. Its authoritative .NET
server, Unity voxel client, procedural galaxy, ship flight, crafting, persistence and multiplayer remain the
technical foundation. `BlocksBeyondTheStars` stays as the internal namespace and data-folder codename for now;
changing it would create a large compatibility migration without improving the first playable experience.

## Player promise

The core loop has three mutually supporting verbs:

1. **Build** — mine, craft, reshape terrain, establish bases and construct ships.
2. **Travel** — launch from a world, cross a star system and land somewhere materially different.
3. **Uncover** — find impossible structures and records whose meaning changes as evidence accumulates.

The horror should come from scale, implication and discovery. It should not turn the sandbox into a linear
quest corridor, and it should not rely on gore or constant jump scares.

## First vertical slice

The foundation slice establishes the game's identity without replacing systems that already work:

- Client, launcher and build metadata show **Voidcraft**.
- Fresh saves select `voidcraft_awakening`, displayed as **Voidcraft: The Sleeper Signal**.
- The first-spawn prologue frames a self-landed ship, an erased log and a signal beneath the player.
- Thirteen threshold-paced story beats reveal the Veyl, the buried Anchors and the Sleeper signal.
- Six world fragments, four personal memories, five NPC flavour lines and a four-node finale argument are
  authored in English and German.
- Finale and one-shot insight text keys now belong to each story pack. A new campaign no longer leaks copy
  from the original VEGA Protocol at its climax.
- The original `vega_protocol` campaign remains installed as a compatibility/optional pack, and `none` remains
  the pure-sandbox choice.

This is a content-complete story foundation, not yet a packaged Voidcraft release. It reuses the existing
fragment placement, combat, milestone, Story Log and Guardian-finale mechanics. It does not yet guarantee a
bespoke signal site in the player's first half hour.

## Canon for this slice

- **The Veyl** did not terraform planets; they assembled world-shells around buried Anchors.
- **The lattice** links those Anchors across star systems and was designed to keep something outside ordinary
  space from noticing inhabited worlds.
- **The Sleeper signal** is not a message. It is attention moving through the lattice.
- **VEGA** is still the ship AI and tutorial voice, but its return record is incomplete and cannot be trusted as
  a complete account of the player's arrival.
- **The Guardian Core / Black Anchor** maintains the old silence through an increasingly destructive mandate.
  The finale asks whether a prison that destroys its inhabitants is still protecting them.

## Next playable milestone

The next slice should make the opening mystery physical and testable:

1. Guarantee one authored **Signal Scar** within walking distance of a fresh spawn.
2. Give it a unique block/material silhouette, an audible pulse and one early fragment.
3. Tie the first craft-and-mine tutorial steps to reaching the Scar without making the tutorial story-specific.
4. Add a real story-pack selector to world creation so Voidcraft, VEGA Protocol and sandbox are all visible.
5. Replace the reused finale chamber/enemies with Anchor-specific geometry and audiovisual language.
6. Run a clean-build playtest and tune the first 30 minutes, fragment density and beat thresholds from evidence.

## Release boundaries

Before publishing a Voidcraft build, the fork needs its own distribution identifiers, update feed, hosted-world
policy, screenshots and release workflow. Upstream service URLs and release links must not be presented as
Voidcraft infrastructure. Upstream copyright, license notices and contributor attribution remain intact.
