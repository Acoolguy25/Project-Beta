#!/usr/bin/env bash
# Run UnityBackend locally for Project-Beta, in its debug mode, without touching its checkout.
#
# Usage: Tools/CloudEnv/backend.sh [start|stop|status|log]   (default: start)
#
# UnityBackend picks its mode from the machine's hostname ("ryan" in it = debug, otherwise prod).
# Prod mode would require real Unity player tokens, bind 0.0.0.0 and launch LinuxServer builds,
# so this script sets the mode before the app loads (BACKEND_MODE, default debug). In debug mode
# it listens on 127.0.0.1:8212 (what LocalNetworkConfig/ServerNetworkConfig point at), accepts the
# Editor's debug tokens, and hands out the server Editor as "unity-test-server" on port 20000.
#
# The backend writes its SQLite database in the working directory, so it runs from a copy of
# game.db in $BACKEND_STATE (default ~/.cache/project-beta-backend) and the git checkout stays clean.
# BACKEND_RESET=1 replaces that copy with the checkout's game.db. BACKEND_UNIVERSES (default
# war_valley) are added to the copy's universe table.
#
# Env: BACKEND_DIR (default ../UnityBackend, set by setup.sh INSTALL_BACKEND=1), BACKEND_MODE
#      (debug|local|prod), BACKEND_STATE, BACKEND_URL (default http://127.0.0.1:8212).
set -euo pipefail
[[ -f /etc/profile.d/project-beta-unity.sh ]] && source /etc/profile.d/project-beta-unity.sh

PROJECT_DIR="${PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
BACKEND_DIR="${BACKEND_DIR:-$(dirname "$PROJECT_DIR")/UnityBackend}"
BACKEND_MODE="${BACKEND_MODE:-debug}"
BACKEND_STATE="${BACKEND_STATE:-$HOME/.cache/project-beta-backend}"
BACKEND_URL="${BACKEND_URL:-http://127.0.0.1:8212}"
PY="$BACKEND_DIR/.venv/bin/python"
PID_FILE="$BACKEND_STATE/backend.pid"
LOG_FILE="$BACKEND_STATE/backend.log"

log() { printf '[backend] %s\n' "$*"; }
die() { printf '[backend] ERROR: %s\n' "$*" >&2; exit 1; }

running() { [[ -f "$PID_FILE" ]] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; }

cmd_start() {
  [[ -x "$PY" ]] || die "no venv at $BACKEND_DIR/.venv; run: sudo INSTALL_BACKEND=1 Tools/CloudEnv/setup.sh"
  [[ "$BACKEND_MODE" =~ ^(debug|local|prod)$ ]] || die "BACKEND_MODE must be debug, local or prod"
  if running; then log "already running (pid $(cat "$PID_FILE")) at $BACKEND_URL"; return; fi
  mkdir -p "$BACKEND_STATE"
  if [[ ! -f "$BACKEND_STATE/game.db" || "${BACKEND_RESET:-0}" == 1 ]]; then
    rm -f "$BACKEND_STATE"/game.db*
    cp "$BACKEND_DIR/game.db" "$BACKEND_STATE/game.db"
  fi
  # The checkout's game.db only lists some universes; add the ones the Editors play (war_valley is
  # what ServerBootStrap hosts and EasyDebug's DebugPlayGame joins) to the local copy.
  "$PY" - "$BACKEND_STATE/game.db" ${BACKEND_UNIVERSES:-war_valley} <<'PY'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
db.executemany("INSERT OR IGNORE INTO universe (universe_id) VALUES (?)", [(u,) for u in sys.argv[2:]])
db.commit()
PY
  # The backend reads the expected client version from LinuxServer/application.json (BUILD_DIR,
  # relative to its working directory), which BuildLocalServer writes next to a server build. Use
  # the checkout's if there is one, else the Linux Server build profile's bundleVersion.
  mkdir -p "$BACKEND_STATE/LinuxServer"
  if [[ -f "$BACKEND_DIR/LinuxServer/application.json" ]]; then
    cp "$BACKEND_DIR/LinuxServer/application.json" "$BACKEND_STATE/LinuxServer/application.json"
  else
    local ver
    ver="$(grep -a -m1 -oE 'bundleVersion: [^'"'"']+' "$PROJECT_DIR/Assets/Settings/Build Profiles/Linux Server.asset" | cut -d' ' -f2)"
    printf '{\n    "ApplicationVersion": "%s"\n}\n' "${ver:?no bundleVersion in the Linux Server build profile}" \
      >"$BACKEND_STATE/LinuxServer/application.json"
  fi
  log "starting UnityBackend ($BACKEND_MODE mode) from $BACKEND_DIR, log $LOG_FILE"
  # Same entry point as UnityBackend's run.sh (python -m net), with the mode set first.
  (cd "$BACKEND_STATE" && PYTHONPATH="$BACKEND_DIR" BACKEND_MODE="$BACKEND_MODE" setsid nohup "$PY" -c '
import os, runpy
from util import mode
mode.set_mode(mode.Mode[os.environ["BACKEND_MODE"]])
runpy.run_module("net", run_name="__main__", alter_sys=True)
' >"$LOG_FILE" 2>&1 </dev/null & echo $! >"$PID_FILE")
  for _ in $(seq 60); do
    curl -fsS -m 2 "$BACKEND_URL/api/universes/v1/list" >/dev/null 2>&1 && break
    running || die "backend exited; see $LOG_FILE"
    sleep 0.5
  done
  curl -fsS -m 2 "$BACKEND_URL/api/universes/v1/list" >/dev/null || die "backend not answering at $BACKEND_URL; see $LOG_FILE"
  log "ready at $BACKEND_URL (universes: $(curl -fsS -m 2 "$BACKEND_URL/api/universes/v1/list"))"
}

cmd_stop() {
  if running; then
    local pid; pid="$(cat "$PID_FILE")"
    kill -- "-$pid" 2>/dev/null || kill "$pid"
    for _ in $(seq 50); do kill -0 "$pid" 2>/dev/null || break; sleep 0.1; done
    log "stopped"
  fi
  rm -f "$PID_FILE"
}

case "${1:-start}" in
  start) cmd_start ;;
  stop) cmd_stop ;;
  status) if running; then log "running (pid $(cat "$PID_FILE")) at $BACKEND_URL"; else log "not running"; exit 1; fi ;;
  log) tail -n "${LINES:-50}" "$LOG_FILE" ;;
  *) die "unknown command '$1'" ;;
esac
