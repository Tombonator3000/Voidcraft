# Voidcraft GitHub Windows playtest

Status: the fork-specific workflow is implemented in
[`.github/workflows/voidcraft-playtest.yml`](../../.github/workflows/voidcraft-playtest.yml). A green run
produces a portable Windows artifact with the Unity player, all content and a bundled local server.
It does not publish a GitHub Release or use an upstream hosted-worlds service.

## One-time fork setup

GitHub forks do not inherit Actions secrets. The repository owner must complete these steps once:

1. Open the repository's **Actions** tab and enable workflows for the fork if GitHub shows the fork-workflow
   warning. In **Settings → Actions → General**, allow GitHub Actions to run.
2. Install Unity Hub locally, sign in, and activate a free Unity Personal license under
   **Preferences → Licenses → Add → Get a free personal license**.
3. Read the activated license file. On Windows it is normally
   `C:\ProgramData\Unity\Unity_lic.ulf`; on macOS it is
   `/Library/Application Support/Unity/Unity_lic.ulf`; on Linux it is
   `~/.local/share/unity3d/Unity/Unity_lic.ulf`.
4. Open **Settings → Secrets and variables → Actions** in the fork and add three repository secrets:

   - `UNITY_LICENSE`: the complete contents of `Unity_lic.ulf`
   - `UNITY_EMAIL`: the Unity account email
   - `UNITY_PASSWORD`: the Unity account password

Never commit these values. The workflow checks only whether they exist and never prints them. The current
activation procedure is maintained in the [GameCI activation guide](https://game.ci/docs/github/activation/).

## Build and download

A push to `feat/voidcraft-playable` starts the workflow automatically. Once the workflow also exists on the
default branch, it can be started manually from **Actions → Voidcraft Windows playtest → Run workflow**.

After both jobs are green, open the run and download
`Voidcraft-Windows-Playtest-<run number>` under **Artifacts**. Extract the entire download and run
`BlocksBeyondTheStars.exe`; the technical executable name is retained because Unity requires its matching
`BlocksBeyondTheStars_Data` folder. Player-facing branding and the save-data identity are Voidcraft.

The build is unsigned, so Windows SmartScreen may warn. The artifact is retained for 30 days. Singleplayer
works offline after download; LAN hosting and direct joins are available, while official hosted worlds are
disabled until Voidcraft operates its own service.

## What the workflow verifies

Before uploading a game, GitHub validates both story packs and locale tables, builds the .NET server/client
test projects with warnings as errors, runs their non-Slow test tier, builds Unity 6000.4.9f1, and checks that
the artifact contains the Windows executable, the bundled server and `voidcraft_awakening` story data.
