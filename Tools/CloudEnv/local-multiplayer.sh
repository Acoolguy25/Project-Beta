#!/usr/bin/env bash
# Run Project-Beta locally as server + client with ParrelSync, no UnityBackend needed.
#
#   main project     -> CLIENT editor  (Windows build profile, plays Assets/Scenes/MainMenu.unity)
#   ParrelSync clone -> SERVER editor  (Linux Server build profile => UNITY_SERVER, plays ServerInit)
#
# Default: direct connection (no UnityBackend). The server binds 127.0.0.1:<port> and the client's
# ClientConnector joins it directly; backend requests are disabled in that mode.
#
# BACKEND=1: also run a local UnityBackend (Tools/CloudEnv/backend.sh, debug mode, 127.0.0.1:8212),
# with direct connection off, so the client logs in and joins war_valley through the backend (the
# main menu's EasyDebug DebugPlayGame does that by itself), and the server Editor registers with it
# as "unity-test-server". Needs setup.sh INSTALL_BACKEND=1.
# The mode is passed to both Editors as PROJECT_BETA_DIRECT_CONNECTION, so LocalNetworkConfig.asset
# isn't edited.
#
# Usage: Tools/CloudEnv/local-multiplayer.sh [import|clone|start|play|shot|stop|all]   (default: all)
#   import  first-time package resolve + asset import of the main project (batchmode, slow once)
#   clone   create the ParrelSync clone with ParrelSync's own ClonesManager API, argument "server"
#   start   start Xvfb displays and both Editors (graphical, so Play mode runs normally)
#   play    open the scenes and enter Play mode: server first, then client
#   shot    screenshot both Editors to $OUT_DIR
#   stop    exit Play mode and close both Editors
#
# Env: PROJECT_DIR (default: this repo), UNITY_EDITOR (from setup.sh), OUT_DIR (default
#      $PROJECT_DIR/Logs/local-multiplayer), SERVER_DISPLAY (:91), CLIENT_DISPLAY (:92),
#      SERVER_SCENE, CLIENT_SCENE, READY_TIMEOUT (seconds, default 1800), BACKEND (0|1)
set -euo pipefail
[[ -f /etc/profile.d/project-beta-unity.sh ]] && source /etc/profile.d/project-beta-unity.sh
export UNITY_NO_CONSENT_PROMPT=1 UNITY_NO_AGENT_HINTS=1

PROJECT_DIR="${PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
CLONE_DIR="${PROJECT_DIR}_clone_0"          # ParrelSync's naming: <project>_clone_<n>
UNITY_EDITOR="${UNITY_EDITOR:?run Tools/CloudEnv/setup.sh first}"
OUT_DIR="${OUT_DIR:-$PROJECT_DIR/Logs/local-multiplayer}"
SERVER_DISPLAY="${SERVER_DISPLAY:-:91}"
CLIENT_DISPLAY="${CLIENT_DISPLAY:-:92}"
SERVER_PROFILE="Assets/Settings/Build Profiles/Linux Server.asset"
# Same profile as on the Windows dev machine. Its bundleVersion must match the server profile's,
# because the server rejects clients whose Application.version differs.
CLIENT_PROFILE="${CLIENT_PROFILE:-Assets/Settings/Build Profiles/Windows.asset}"
SERVER_SCENE="${SERVER_SCENE:-Assets/Scenes/ServerInit.unity}"
CLIENT_SCENE="${CLIENT_SCENE:-Assets/Scenes/MainMenu.unity}"
READY_TIMEOUT="${READY_TIMEOUT:-1800}"
BACKEND="${BACKEND:-0}"
if [[ "$BACKEND" == 1 ]]; then export PROJECT_BETA_DIRECT_CONNECTION=0; else export PROJECT_BETA_DIRECT_CONNECTION=1; fi
BACKEND_SH="$(dirname "${BASH_SOURCE[0]}")/backend.sh"
mkdir -p "$OUT_DIR"

log() { printf '[local-mp] %s\n' "$*"; }
die() { printf '[local-mp] ERROR: %s\n' "$*" >&2; exit 1; }
U() { unity --no-banner --non-interactive "$@"; }

batch() { # project logname args...
  local proj="$1" name="$2"; shift 2
  log "batchmode ($name) on $proj -> $OUT_DIR/$name.log"
  "$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$proj" -logFile "$OUT_DIR/$name.log" "$@" \
    || die "batchmode $name failed; see $OUT_DIR/$name.log"
}

cmd_import() {
  [[ -f "$PROJECT_DIR/Library/.cloudenv-imported" ]] && { log "main project already imported"; return; }
  batch "$PROJECT_DIR" import
  touch "$PROJECT_DIR/Library/.cloudenv-imported"
}

link_clone_packages() {
  # ParrelSync links Assets and ProjectSettings into the clone but copies Packages, so embedded
  # packages added on main later (com.ryan.*) are missing from the clone and the server Editor
  # fails to compile. Link Packages too, so the clone always matches the main project.
  [[ -L "$CLONE_DIR/Packages" ]] && return
  log "linking the clone's Packages to $PROJECT_DIR/Packages"
  rm -rf "$CLONE_DIR/Packages"
  ln -s "$PROJECT_DIR/Packages" "$CLONE_DIR/Packages"
}

cmd_clone() {
  if [[ -f "$CLONE_DIR/.clone" ]]; then
    log "ParrelSync clone exists: $CLONE_DIR"
  else
    cmd_import
    batch "$PROJECT_DIR" clone -executeMethod ParrelSync.ClonesManager.CreateCloneFromCurrent
    [[ -f "$CLONE_DIR/.clone" ]] || die "ParrelSync did not create $CLONE_DIR"
  fi
  # ParrelSync copies Packages/, but the shared code now lives in embedded Packages/com.ryan.*:
  # link it like Assets/ and ProjectSettings/ so the clone compiles the same packages.
  if [[ -d "$CLONE_DIR/Packages" && ! -L "$CLONE_DIR/Packages" ]]; then
    rm -rf "$CLONE_DIR/Packages"
    ln -s "$PROJECT_DIR/Packages" "$CLONE_DIR/Packages"
  fi
  link_clone_packages
  # ClonesManager.GetArgument() reads this; EditorPlayerIdSpoof uses it as the player id.
  printf 'server' >"$CLONE_DIR/.parrelsyncarg"
  # Switch the clone's own Library to the Linux Server profile once, in batchmode.
  if [[ ! -f "$CLONE_DIR/Library/.server-profile" ]]; then
    batch "$CLONE_DIR" clone-server-profile -activeBuildProfile "$SERVER_PROFILE"
    touch "$CLONE_DIR/Library/.server-profile"
  fi
}

start_display() { # display
  local d="$1" n="${1#:}"
  if ! xdpyinfo -display "$d" >/dev/null 2>&1; then
    rm -f "/tmp/.X11-unix/X$n" "/tmp/.X$n-lock"   # stale socket from a dead Xvfb
    setsid nohup Xvfb "$d" -screen 0 1600x900x24 -nolisten tcp >"$OUT_DIR/xvfb$n.log" 2>&1 </dev/null &
    for _ in $(seq 50); do xdpyinfo -display "$d" >/dev/null 2>&1 && break; sleep 0.2; done
  fi
  xdpyinfo -display "$d" >/dev/null 2>&1 || die "Xvfb $d did not start; see $OUT_DIR/xvfb$n.log"
}

check_terms() {
  # Unity 6.6+ shows a blocking "Unity Editor Software Terms" dialog until they're accepted for
  # this Editor version. Accepting is the account owner's decision, never this script's.
  if U terms status --format tsv --no-header 2>/dev/null | awk -F"\t" -v v="$UNITY_VERSION" '$1==v && $3=="not-accepted"{f=1} END{exit !f}'; then
    die "Unity Editor Software Terms not accepted for $UNITY_VERSION. The account owner runs: unity terms accept $UNITY_VERSION --yes"
  fi
}

dismiss_root_warning() { # display
  # Graphical Editors started as root (as in cloud containers) first show a "You appear to be
  # running Unity as root" dialog. Click its "Continue anyway" button when it appears.
  local d="$1" w
  for _ in $(seq 120); do
    w="$(DISPLAY="$d" xdotool search --name '^Warning$' 2>/dev/null | head -n1 || true)"
    if [[ -n "$w" ]]; then
      DISPLAY="$d" xdotool mousemove --window "$w" 280 100 click 1
      log "dismissed root warning on $d"; return
    fi
    sleep 1
  done
}

start_editor() { # display project logname extra-args...
  local d="$1" proj="$2" name="$3"; shift 3
  if U status --project-path "$proj" >/dev/null 2>&1; then log "$name Editor already running"; return; fi
  # An Editor that was killed leaves Temp/__Backupscenes, and the next start stops on a modal
  # "Scene Backup Detected" dialog. These Editors only ever hold scenes opened by this script.
  rm -rf "$proj/Temp/__Backupscenes"
  log "starting $name Editor on $d ($proj)"
  DISPLAY="$d" setsid nohup "$UNITY_EDITOR" -projectPath "$proj" -logFile "$OUT_DIR/$name-editor.log" "$@" \
    >/dev/null 2>&1 </dev/null &
  [[ $EUID -eq 0 ]] && dismiss_root_warning "$d" &
}

wait_ready() { # project name
  log "waiting for $2 Editor to be ready (up to ${READY_TIMEOUT}s)"
  U status --until-ready --timeout "$READY_TIMEOUT" --project-path "$1" >/dev/null \
    || die "$2 Editor not ready; see $OUT_DIR/$2-editor.log"
}

cmd_start() {
  check_terms
  [[ -f "$CLONE_DIR/.clone" ]] || cmd_clone
  link_clone_packages
  [[ "$BACKEND" == 1 ]] && "$BACKEND_SH" start
  start_display "$SERVER_DISPLAY"; start_display "$CLIENT_DISPLAY"
  start_editor "$SERVER_DISPLAY" "$CLONE_DIR" server -activeBuildProfile "$SERVER_PROFILE"
  start_editor "$CLIENT_DISPLAY" "$PROJECT_DIR" client -activeBuildProfile "$CLIENT_PROFILE"
  wait_ready "$CLONE_DIR" server
  wait_ready "$PROJECT_DIR" client
}

eval_cs() { # project code
  U command eval --caller plugin --skill unity-cli --project-path "$1" --code "$2"
}

ucmd() { # project command args...   (Pipeline commands exposed by the connected Editor)
  local p="$1"; shift
  U command "$@" --caller plugin --skill unity-cli --project-path "$p"
}

play() { # project scene name
  log "$3: open $2 and enter Play mode"
  ucmd "$1" open_scene --path "$2" >/dev/null
  ucmd "$1" editor_play >/dev/null
}

wait_log() { # file pattern seconds
  local t=0
  until grep -qE "$2" "$1" 2>/dev/null; do
    sleep 2; t=$((t + 2)); ((t < $3)) || return 1
  done
}

cmd_play() {
  play "$CLONE_DIR" "$SERVER_SCENE" server
  wait_log "$OUT_DIR/server-editor.log" "Hosting at|Server has started" 300 \
    || log "warning: no 'Hosting at' line in server log yet"
  play "$PROJECT_DIR" "$CLIENT_SCENE" client
  wait_log "$OUT_DIR/client-editor.log" "Connecting To 127.0.0.1" 300 \
    || log "warning: client has not logged a connection attempt yet"
  sleep 15
  log "server log tail:"; grep -E "Hosting|Server|connect|Exception|Error" "$OUT_DIR/server-editor.log" | tail -n 15 || true
  log "client log tail:"; grep -E "Connect|connect|Exception|Error" "$OUT_DIR/client-editor.log" | tail -n 15 || true
  [[ "$BACKEND" == 1 ]] && { log "backend log tail:"; "$BACKEND_SH" log | grep -v 'universes/v1/list' | tail -n 15 || true; }
  return 0
}

cmd_shot() {
  local ts; ts="$(date +%H%M%S)"
  import -display "$SERVER_DISPLAY" -window root "$OUT_DIR/server-$ts.png"
  import -display "$CLIENT_DISPLAY" -window root "$OUT_DIR/client-$ts.png"
  log "screenshots: $OUT_DIR/server-$ts.png $OUT_DIR/client-$ts.png"
}

cmd_stop() {
  for p in "$PROJECT_DIR" "$CLONE_DIR"; do
    ucmd "$p" editor_stop >/dev/null 2>&1 || true
    U close "$p" >/dev/null 2>&1 || true
  done
  [[ "$BACKEND" == 1 ]] && "$BACKEND_SH" stop
  return 0
}

case "${1:-all}" in
  import) cmd_import ;;
  clone) cmd_clone ;;
  start) cmd_start ;;
  play) cmd_play ;;
  shot) cmd_shot ;;
  stop) cmd_stop ;;
  all) cmd_clone; cmd_start; cmd_play; cmd_shot ;;
  *) die "unknown step '$1'" ;;
esac
