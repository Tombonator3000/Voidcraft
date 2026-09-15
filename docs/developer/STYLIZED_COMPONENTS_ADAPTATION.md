# Stylized Components adaptation notes

Voidcraft's interactive biome-surface work was informed by Christian Ortiz (Cortiz)' public **stylized-components** project:

- https://github.com/cortiz2894/stylized-components
- MIT License, copyright (c) 2026 Christian Ortiz (Cortiz)

The upstream project demonstrates reusable Three.js / React Three Fiber rendering studies including instanced grass, a shared procedural ground/vegetation mask, root-pinned wind, object trampling, thin-vegetation back-lighting, water interaction rings, depth intersections and a GPU wave simulation.

## What Voidcraft adapts

Voidcraft uses those ideas as rendering research and implements its own Unity 6 C#/HLSL systems that fit the existing voxel renderer:

- one deterministic world-space biome mask drives both grass-to-earth blending and vegetation density;
- near-field shell turf is pressed down and splayed around the local player;
- High quality adds deterministic GPU-instanced cross-blades sampled from actual grass top triangles;
- rare flower-tip variation is generated from the same world-space seed;
- vegetation uses root-pinned wind and restrained sun-facing transmission;
- water interaction uses a bounded event history that draws fading double-crested rings on the existing voxel water surface;
- the existing Voidcraft water shader remains responsible for depth, shoreline foam, refraction and SSR.

No Three.js, React, TypeScript source, GLB models, textures, demo media, or other upstream assets are vendored into Voidcraft. The Unity implementations were written for Voidcraft's existing architecture and quality-preset budget.

## Performance policy

- Potato / Low: existing voxel surfaces only.
- Medium: shared biome mask + short shell turf + bounded water interaction rings.
- High: Medium features plus near-field GPU-instanced grass blades.
- Reduced Effects and space flight disable these surface embellishments.

This file intentionally retains attribution to the upstream study even though the implementation is original and does not vendor upstream source.
