#!/usr/bin/env python3
"""Run the opt-in real-input survey expedition, then verify its save in another player process.

Uses a new XDG profile and ordinary singleplayer hosting. Never writes game settings, inventory,
player pose or saves. Keep the private profile outside collected evidence. This is journey evidence,
not a performance measurement. A surface-only run deliberately returns 2 (partial).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def run_player(player: Path, output: Path, env: dict[str, str], args: list[str]) -> dict:
    output.mkdir()
    command = [str(player), "-verifySurveyJourney", "-journeyOut", str(output),
               "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
               "-logFile", str(output / "player.log"), *args]
    started = time.time()
    timed_out = False
    with (output / "stdout.log").open("w", encoding="utf-8") as stream:
        process = subprocess.Popen(command, cwd=player.parent, env=env, stdout=stream,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            code = process.wait(timeout=1350)
        except subprocess.TimeoutExpired:
            timed_out = True
            os.killpg(process.pid, signal.SIGTERM)
            try:
                code = process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                code = process.wait(timeout=10)
    run = {"command": command, "processId": process.pid, "exitCode": code,
           "seconds": round(time.time() - started, 3), "timedOut": timed_out}
    write_json(output / "process.json", run)
    result_path = output / "result.json"
    if result_path.is_file():
        try:
            run["result"] = json.loads(result_path.read_text(encoding="utf-8"))
        except (ValueError, OSError) as error:
            run["invalidResult"] = str(error)
    return run


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True, help="New evidence directory; must not exist.")
    parser.add_argument("--profile", type=Path, help="New private XDG profile outside evidence; must not exist.")
    parser.add_argument("--seed", type=int, default=4242)
    parser.add_argument("--surface-only", action="store_true", help="Verify the prefix, report partial, exit 2.")
    options = parser.parse_args()
    player, output = options.player.resolve(), options.out.resolve()
    if not player.is_file() or not os.access(player, os.X_OK):
        parser.error("--player must be an executable Linux player")
    if output.exists():
        parser.error("--out already exists; evidence is never overwritten")
    if options.profile:
        profile = options.profile.resolve()
        if profile.exists() or profile == output or output in profile.parents:
            parser.error("--profile must be a new directory outside evidence")
        profile.mkdir(parents=True, mode=0o700)
    else:
        profile = Path(tempfile.mkdtemp(prefix="voidcraft-journey-profile-"))
    output.mkdir(parents=True)
    env = os.environ.copy()
    for variable, directory in (("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache")):
        location = profile / directory
        location.mkdir(mode=0o700)
        env[variable] = str(location)
    # Hash only shipped payload files, never profile settings, credentials or generated saves.
    data = player.parent / (player.stem + "_Data")
    if not data.is_dir():
        candidates = sorted(player.parent.glob("*_Data"))
        data = candidates[0] if len(candidates) == 1 else data
    files = [player, *sorted((data / "Managed").glob("BlocksBeyondTheStars*.dll")),
             *sorted((data / "StreamingAssets" / "server").glob("BlocksBeyondTheStars*.dll"))]
    write_json(output / "payload.json", {str(path.relative_to(player.parent)): hashlib.sha256(path.read_bytes()).hexdigest()
                                       for path in files if path.is_file()})
    acquire_args = ["-journeyMode", "acquire", "-journeySeed", str(options.seed)]
    if options.surface_only:
        acquire_args += ["-journeyCheckpoint", "surface"]
    acquisition = run_player(player, output / "acquire", env, acquire_args)
    result = acquisition.get("result", {})
    if options.surface_only:
        partial = (acquisition["exitCode"] == 2 and result.get("status") == "partial"
                   and result.get("surfaceScan") and result.get("inputIsolationVerified"))
        write_json(output / "journey.json", {"status": "partial" if partial else "failed", "journeyComplete": False,
                                            "acquire": acquisition})
        return 2 if partial else 1
    if (acquisition["exitCode"] != 0 or result.get("status") != "acquired"
            or not result.get("shutdownGraceful") or not result.get("inputIsolationVerified")):
        write_json(output / "journey.json", {"status": "failed", "journeyComplete": False, "acquire": acquisition})
        return 1
    reload_run = run_player(player, output / "reload", env,
                            ["-journeyMode", "reload", "-journeyAcquireResult", str(output / "acquire" / "result.json")])
    restored = reload_run.get("result", {})
    passed = (reload_run["exitCode"] == 0 and restored.get("status") == "passed"
              and restored.get("journeyComplete") and restored.get("shutdownGraceful")
              and restored.get("inputIsolationVerified")
              and restored.get("rewardCount") == result.get("rewardCount") == 1
              and reload_run["processId"] != acquisition["processId"])
    write_json(output / "journey.json", {"status": "passed" if passed else "failed", "journeyComplete": bool(passed),
                                        "acquire": acquisition, "reload": reload_run})
    print(f"Journey {'passed' if passed else 'failed'}; evidence: {output}")
    print(f"Private test profile retained at: {profile}")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
