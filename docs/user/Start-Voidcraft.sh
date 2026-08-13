#!/usr/bin/env bash
# Starts the portable Voidcraft playtest from its own folder on Linux.
set -euo pipefail

GAME_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$GAME_DIR"

chmod +x ./BlocksBeyondTheStars.x86_64
chmod +x ./BlocksBeyondTheStars_Data/StreamingAssets/server/BlocksBeyondTheStars.GameServer
exec ./BlocksBeyondTheStars.x86_64 "$@"
