# Voidcraft GitHub desktop playtest

Status: the fork-specific workflow is implemented in
[`.github/workflows/voidcraft-playtest.yml`](../../.github/workflows/voidcraft-playtest.yml). A green run
produces portable Windows and Kubuntu/Linux artifacts with the Unity player, all content and a bundled
platform-native local server.
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
default branch, it can be started manually from **Actions → Voidcraft desktop playtest → Run workflow**.

After validation and the relevant platform job are green, open the run's **Artifacts** section:

- **Windows:** download `Voidcraft-Windows-Playtest-<run number>`, extract the entire ZIP and run
  `BlocksBeyondTheStars.exe`.
- **Kubuntu/Linux:** download `Voidcraft-Kubuntu-Playtest-<run number>` and extract the outer ZIP. It contains
  a same-named `.tar.gz`; extract that tarball into a new folder and run `./Start-Voidcraft.sh`. The tarball
  is required because GitHub's artifact ZIP does not preserve Linux executable permissions.

The technical executable name is retained because Unity requires its matching `BlocksBeyondTheStars_Data`
folder. Player-facing branding and the save-data identity are Voidcraft.

The builds are unsigned, so Windows SmartScreen may warn. Both artifacts are retained for 30 days.
Singleplayer works offline after download; LAN hosting and direct joins are available, while official hosted
worlds are disabled until Voidcraft operates its own service.

## What the workflow verifies

Before uploading a game, GitHub validates both story packs and locale tables, builds the .NET server/client
test projects with warnings as errors, runs their non-Slow test tier, builds Unity 6000.4.9f1 for Windows and
Linux, and checks that each artifact contains its player executable, bundled platform-native server and
`voidcraft_awakening` story data. The Linux job also verifies its tarball before upload.
