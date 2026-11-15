#!/usr/bin/env bash
# Run UnityBackend locally for Project-Beta in its debug mode, without touching its checkout.
#
# Usage: Tools/CloudEnv/backend.sh [start|stop|status|log]   (default: start)
#
# Debug mode (UNITYBACKEND_MODE=debug): listens on 127.0.0.1:8212, which LocalNetworkConfig and
# ServerNetworkConfig already point at; the bearer token is the player id and there's no client
# version check; every player is sent to the server Editor as "unity-test-server" on port 20000.
#
# The database lives in $BACKEND_STATE (default ~/.cache/project-beta-backend) and the backend runs
# from there, so the checkout stays clean (no data/, no config.toml picked up from it).
# BACKEND_RESET=1 deletes that database first. BACKEND_UNIVERSES (default war_valley, which the
# server Editor hosts and EasyDebug's DebugPlayGame joins) are added with `unitybackend universe add`.
#
# Env: BACKEND_DIR (default ../UnityBackend, set by setup.sh INSTALL_BACKEND=1), BACKEND_MODE
#      (debug|local|prod, default debug), BACKEND_STATE, BACKEND_PORT (default 8212).
set -euo pipefail
[[ -f /etc/profile.d/project-beta-unity.sh ]] && source /etc/profile.d/project-beta-unity.sh

PROJECT_DIR="${PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
BACKEND_DIR="${BACKEND_DIR:-$(dirname "$PROJECT_DIR")/UnityBackend}"
BACKEND_STATE="${BACKEND_STATE:-$HOME/.cache/project-beta-backend}"
BACKEND_PORT="${BACKEND_PORT:-8212}"
BACKEND_URL="http://127.0.0.1:$BACKEND_PORT"
UB="$BACKEND_DIR/.venv/bin/unitybackend"
PID_FILE="$BACKEND_STATE/backend.pid"
LOG_FILE="$BACKEND_STATE/backend.log"

export UNITYBACKEND_MODE="${BACKEND_MODE:-debug}"
export UNITYBACKEND_PORT="$BACKEND_PORT"
export UNITYBACKEND_DATA_DIR="$BACKEND_STATE/data"
export UNITYBACKEND_CONFIG="$BACKEND_STATE/config.toml"   # empty: don't read /etc/unitybackend

log() { printf '[backend] %s\n' "$*"; }
die() { printf '[backend] ERROR: %s\n' "$*" >&2; exit 1; }

running() { [[ -f "$PID_FILE" ]] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; }
answering() { curl -fsS -m 2 "$BACKEND_URL/api/universes/v1/list" 2>/dev/null; }

cmd_start() {
  [[ -x "$UB" ]] || die "no unitybackend in $BACKEND_DIR/.venv; run: sudo INSTALL_BACKEND=1 Tools/CloudEnv/setup.sh"
  if running; then log "already running (pid $(cat "$PID_FILE")) at $BACKEND_URL"; return; fi
  mkdir -p "$BACKEND_STATE"
  [[ -f "$UNITYBACKEND_CONFIG" ]] || : >"$UNITYBACKEND_CONFIG"
  [[ "${BACKEND_RESET:-0}" == 1 ]] && rm -rf "$UNITYBACKEND_DATA_DIR"
  (cd "$BACKEND_STATE" && "$UB" migrate >/dev/null)
  for u in ${BACKEND_UNIVERSES:-war_valley}; do
    (cd "$BACKEND_STATE" && "$UB" universe list | grep -qx "$u" || "$UB" universe add "$u" >/dev/null)
  done
  log "starting UnityBackend ($UNITYBACKEND_MODE mode) from $BACKEND_DIR, log $LOG_FILE"
  (cd "$BACKEND_STATE" && setsid nohup "$UB" serve >"$LOG_FILE" 2>&1 </dev/null & echo $! >"$PID_FILE")
  for _ in $(seq 60); do
    answering >/dev/null && break
    running || die "backend exited; see $LOG_FILE"
    sleep 0.5
  done
  answering >/dev/null || die "backend not answering at $BACKEND_URL; see $LOG_FILE"
  log "ready at $BACKEND_URL (universes: $(answering))"
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
