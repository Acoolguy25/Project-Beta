#!/usr/bin/env bash
# Project-Beta cloud dev environment setup (Ubuntu 24.04, run as root).
#
# Installs, idempotently:
#   1. System libraries Unity's Linux Editor needs, plus Xvfb for running it without a display.
#   2. The exact Unity Editor version pinned in ProjectSettings/ProjectVersion.txt (version AND
#      changeset), downloaded straight from Unity's CDN and checked against the md5 in Unity's
#      own release manifest (unity-<version>-linux.ini).
#   3. The Linux Dedicated Server build-support module (the server role uses the Linux Server
#      build profile, which needs it).
#   4. The Unity CLI (https://unity.com/install.sh), used for licensing and to drive Editors.
#   5. Optionally (INSTALL_BACKEND=1) the UnityBackend repo and its Python venv.
#
# ParrelSync itself is a UPM git dependency already in Packages/manifest.json
# (https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync), so Unity's package manager
# fetches it on first import; Tools/CloudEnv/local-multiplayer.sh creates the clone.
#
# Usable as a Claude Code cloud environment "Setup script" (no secrets are needed here; the
# license is activated per session by Tools/CloudEnv/license.sh).
#
# Environment overrides:
#   PROJECT_DIR         Project-Beta checkout (default: auto-detect, else shallow sparse clone)
#   PROJECT_REPO        git URL used only when no checkout is found
#   UNITY_VERSION       override (default: m_EditorVersion from ProjectVersion.txt)
#   UNITY_CHANGESET     override (default: revision from m_EditorVersionWithRevision)
#   UNITY_INSTALL_ROOT  default /opt/unity/editors  -> $UNITY_INSTALL_ROOT/<version>/Editor/Unity
#   INSTALL_BACKEND     1 to also clone UnityBackend (if missing) and set up its Python venv (default 0)
#   BACKEND_DIR         UnityBackend checkout (default: next to the project, ../UnityBackend)
#   BACKEND_REPO        git URL used when BACKEND_DIR has no checkout
#   SKIP_UNITY_CLI      1 to skip installing the Unity CLI
set -euo pipefail

log() { printf '\n[setup] %s\n' "$*"; }
die() { printf '[setup] ERROR: %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root"

UNITY_INSTALL_ROOT="${UNITY_INSTALL_ROOT:-/opt/unity/editors}"
PROJECT_REPO="${PROJECT_REPO:-https://github.com/Acoolguy25/Project-Beta.git}"
INSTALL_BACKEND="${INSTALL_BACKEND:-0}"
BACKEND_REPO="${BACKEND_REPO:-https://github.com/Acoolguy25/UnityBackend.git}"
UNITY_CDN="https://download.unity3d.com/download_unity"

# ---------------------------------------------------------------- locate project + version
find_project() {
  local here
  here="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." 2>/dev/null && pwd || true)"
  for d in "${PROJECT_DIR:-}" "$here" /home/user/Project-Beta "$PWD"; do
    [[ -n "$d" && -f "$d/ProjectSettings/ProjectVersion.txt" ]] && { echo "$d"; return; }
  done
}

PROJECT_DIR="$(find_project || true)"
VERSION_FILE=""
if [[ -n "$PROJECT_DIR" ]]; then
  VERSION_FILE="$PROJECT_DIR/ProjectSettings/ProjectVersion.txt"
else
  # Setup scripts can run before the repo is cloned: fetch only ProjectSettings from git.
  tmp="$(mktemp -d)"
  log "No checkout found; reading ProjectVersion.txt from $PROJECT_REPO"
  git clone -q --depth 1 --filter=blob:none --sparse "$PROJECT_REPO" "$tmp/pb"
  git -C "$tmp/pb" sparse-checkout set ProjectSettings
  VERSION_FILE="$tmp/pb/ProjectSettings/ProjectVersion.txt"
fi

if [[ -z "${UNITY_VERSION:-}" || -z "${UNITY_CHANGESET:-}" ]]; then
  line="$(grep '^m_EditorVersionWithRevision:' "$VERSION_FILE")" || die "no m_EditorVersionWithRevision in $VERSION_FILE"
  # m_EditorVersionWithRevision: 6000.5.5f1 (d16e074b49fd)
  UNITY_VERSION="${UNITY_VERSION:-$(sed -E 's/^[^:]+: *([^ ]+) .*/\1/' <<<"$line")}"
  UNITY_CHANGESET="${UNITY_CHANGESET:-$(sed -E 's/.*\(([0-9a-f]+)\).*/\1/' <<<"$line")}"
fi
[[ "$UNITY_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+$ ]] || die "bad Unity version '$UNITY_VERSION'"
[[ "$UNITY_CHANGESET" =~ ^[0-9a-f]{12}$ ]] || die "bad changeset '$UNITY_CHANGESET'"
log "Unity $UNITY_VERSION ($UNITY_CHANGESET) from ${VERSION_FILE}"

EDITOR_ROOT="$UNITY_INSTALL_ROOT/$UNITY_VERSION"
UNITY_BIN="$EDITOR_ROOT/Editor/Unity"

# ---------------------------------------------------------------- system packages
log "Installing system packages"
export DEBIAN_FRONTEND=noninteractive
PKGS=(
  ca-certificates curl git git-lfs xz-utils python3 python3-venv
  xvfb xauth x11-utils x11-apps imagemagick
  libgtk-3-0t64 libglu1-mesa libgl1-mesa-dri libegl1 libvulkan1 mesa-vulkan-drivers
  libnss3 libasound2t64 libgbm1 libxss1 libxtst6 libxrandr2 libxcursor1 libxi6
  libsecret-1-0 libcap2 libxkbcommon0 libdbus-1-3 cpio libarchive-tools xdotool
)
missing=()
for p in "${PKGS[@]}"; do dpkg -s "$p" >/dev/null 2>&1 || missing+=("$p"); done
if ((${#missing[@]})); then
  apt-get update -q
  apt-get install -y -q --no-install-recommends "${missing[@]}"
fi
git lfs install --system >/dev/null 2>&1 || true

# ---------------------------------------------------------------- Unity Editor + module
INI_URL="$UNITY_CDN/$UNITY_CHANGESET/unity-$UNITY_VERSION-linux.ini"
INI="$(curl -fsSL "$INI_URL")" || die "release manifest not found: $INI_URL (version/changeset mismatch?)"

ini_get() { # section key
  awk -v s="[$1]" -v k="$2" '$0==s{f=1;next} /^\[/{f=0} f && index($0,k"=")==1{print substr($0,length(k)+2)}' <<<"$INI"
}

install_component() { # section [dest-for-.pkg]
  local section="$1" dest="${2:-}" url md5 got tmpd
  url="$(ini_get "$section" url)"; md5="$(ini_get "$section" md5)"
  [[ -n "$url" && -n "$md5" ]] || die "section [$section] missing from $INI_URL"
  url="$UNITY_CDN/$UNITY_CHANGESET/$url"
  log "Downloading [$section] $url ($(ini_get "$section" size) bytes)"
  tmpd="$(mktemp -d)"
  mkdir -p "$EDITOR_ROOT"
  if [[ "$url" == *.tar.xz ]]; then
    # Stream: download once, hash and extract at the same time (no 4 GB archive on disk).
    curl -fsSL --retry 5 --retry-delay 5 "$url" \
      | tee >(md5sum | cut -d' ' -f1 >"$tmpd/md5") \
      | tar -xJ -C "$EDITOR_ROOT"
    sleep 1
    got="$(cat "$tmpd/md5")"
  else
    # Unity's Linux manifest serves Windows modules as macOS .pkg files (xar archive holding a
    # gzip'd cpio Payload), the same files Unity Hub on Linux installs. Unpack the Payload into
    # the PlaybackEngines folder that the pkg's install-location names.
    curl -fsSL --retry 5 --retry-delay 5 "$url" -o "$tmpd/m.pkg"
    got="$(md5sum "$tmpd/m.pkg" | cut -d' ' -f1)"
  fi
  [[ "$got" == "$md5" ]] || { rm -rf "$tmpd"; die "[$section] md5 mismatch: expected $md5 got $got"; }
  log "[$section] md5 OK ($md5)"
  if [[ "$url" == *.pkg ]]; then
    mkdir -p "$tmpd/x" "$dest"
    bsdtar -xf "$tmpd/m.pkg" -C "$tmpd/x"
    payload="$(find "$tmpd/x" -type f -name '*Payload' | head -n1)"
    [[ -n "$payload" ]] || die "[$section] no Payload in pkg"
    (cd "$dest" && gzip -dc "$payload" | cpio -idm --quiet)
  fi
  rm -rf "$tmpd"
}

PE="$EDITOR_ROOT/Editor/Data/PlaybackEngines"
if [[ -x "$UNITY_BIN" ]]; then
  log "Editor already installed at $UNITY_BIN"
else
  install_component Unity
fi
if [[ -f "$EDITOR_ROOT/.linux-server-installed" ]]; then
  log "Linux Dedicated Server module already installed"
else
  install_component Linux-Server
  touch "$EDITOR_ROOT/.linux-server-installed"
fi
# Windows modules, unpacked into PlaybackEngines/WindowsStandaloneSupport:
#  - Windows-Server: some asmdefs exclude "WindowsStandalone64Server", and the Editor rejects
#    that platform name (and refuses to compile) unless this module is installed.
#  - Windows-Mono: the client Editor uses the project's "Windows" build profile, like on the
#    Windows dev machine. Its bundleVersion is what the server compares against at login.
for mod in Windows-Server Windows-Mono; do
  var="$(tr 'a-z-' 'A-Z_' <<<"$mod")_MODULE"   # WINDOWS_SERVER_MODULE / WINDOWS_MONO_MODULE
  [[ "${!var:-1}" == 1 ]] || continue
  marker="$EDITOR_ROOT/.$(tr 'A-Z' 'a-z' <<<"$mod")-installed"
  if [[ -f "$marker" ]]; then
    log "$mod module already installed"
  else
    install_component "$mod" "$PE/WindowsStandaloneSupport"
    touch "$marker"
  fi
done
ln -sfn "$EDITOR_ROOT" "$UNITY_INSTALL_ROOT/current"

# ---------------------------------------------------------------- Unity CLI
if [[ "${SKIP_UNITY_CLI:-0}" != 1 ]]; then
  if ! command -v unity >/dev/null 2>&1; then
    # Same source and checks as https://unity.com/install.sh, without running a remote script:
    # read the channel manifest, download the linux-x64 binary, verify its sha256.
    CLI_CDN="https://public-cdn.cloud.unity3d.com/hub/prod/cli"
    manifest="$(curl -fsSL "$CLI_CDN/${UNITY_CLI_VERSION:+$UNITY_CLI_VERSION/}latest.json")"
    read -r cli_ver cli_file cli_sha < <(python3 -c 'import json,sys; m=json.load(sys.stdin); b=m["binaries"]["linux-x64"]; print(m["version"], b["filename"], b["sha256"])' <<<"$manifest")
    log "Installing Unity CLI $cli_ver from $CLI_CDN/$cli_ver/$cli_file"
    curl -fsSL --retry 3 "$CLI_CDN/$cli_ver/$cli_file" -o /usr/local/bin/unity.tmp
    echo "$cli_sha  /usr/local/bin/unity.tmp" | sha256sum -c --quiet - || die "Unity CLI sha256 mismatch"
    chmod +x /usr/local/bin/unity.tmp && mv /usr/local/bin/unity.tmp /usr/local/bin/unity
  fi
  if command -v unity >/dev/null 2>&1; then
    UNITY_NO_CONSENT_PROMPT=1 unity editors add "$UNITY_BIN" --non-interactive --no-banner >/dev/null 2>&1 || true
    # Stage the licensing client while we still have the network (exit 4 = no license yet: expected).
    UNITY_NO_CONSENT_PROMPT=1 unity license status --non-interactive --no-banner >/dev/null 2>&1 || true
  fi
fi

# ---------------------------------------------------------------- Git LFS assets
# ~1,100 assets (models, textures, audio) are LFS pointers until pulled; Unity can't import pointers.
# The pull keeps a second copy of every object in .git/lfs/objects (~6.5 GB). On a fresh cloud
# container nothing there is unpushed, so LFS_TRIM_CACHE=1 (default) deletes that cache once every
# file is checked out. Set LFS_TRIM_CACHE=0 on a machine where you commit new LFS files offline.
if [[ -n "$PROJECT_DIR" && -d "$PROJECT_DIR/.git" && "${SKIP_LFS:-0}" != 1 ]]; then
  # Pull only files still checked out as pointers ("-" in ls-files), so a trimmed cache isn't
  # re-downloaded in full on the next run.
  mapfile -t lfs_missing < <(git -C "$PROJECT_DIR" lfs ls-files | sed -nE 's/^[0-9a-f]+ - (.*)$/\1/p')
  if ((${#lfs_missing[@]})); then
    log "Pulling ${#lfs_missing[@]} Git LFS files"
    if ((${#lfs_missing[@]} > 200)); then
      git -C "$PROJECT_DIR" lfs pull
    else
      git -C "$PROJECT_DIR" lfs pull --include="$(IFS=,; echo "${lfs_missing[*]}")" --exclude=""
    fi
  fi
  if [[ "${LFS_TRIM_CACHE:-1}" == 1 ]] && ! git -C "$PROJECT_DIR" lfs ls-files | grep -qE '^[0-9a-f]+ - '; then
    lfs_dir="$(git -C "$PROJECT_DIR" rev-parse --path-format=absolute --git-common-dir)/lfs/objects"
    [[ -d "$lfs_dir" ]] && { log "Trimming LFS object cache ($(du -sh "$lfs_dir" | cut -f1))"; rm -rf "$lfs_dir" || log "warning: could not fully trim $lfs_dir (a git process may be using it)"; }
  fi
fi

# ---------------------------------------------------------------- env file for later shells
cat >/etc/profile.d/project-beta-unity.sh <<EOF
export UNITY_VERSION="$UNITY_VERSION"
export UNITY_CHANGESET="$UNITY_CHANGESET"
export UNITY_EDITOR="$UNITY_BIN"
export UNITY_NO_CONSENT_PROMPT=1
export UNITY_NO_AGENT_HINTS=1
# Use the Editor's bundled licensing client, so the CLI doesn't need to download one from
# public-stevedore.unity3d.com (blocked on some networks: "Can't reach the Unity licensing client").
export UNITY_CLI_INTERNAL_LICENSING_CLIENT_PATH="$(dirname "$UNITY_BIN")/Data/Resources/Licensing/Client/Unity.Licensing.Client"
EOF

# ---------------------------------------------------------------- optional: UnityBackend
if [[ "$INSTALL_BACKEND" == 1 ]]; then
  if [[ -z "${BACKEND_DIR:-}" ]]; then
    case "$PROJECT_DIR" in ""|/tmp/*) BACKEND_DIR=/home/user/UnityBackend ;; *) BACKEND_DIR="$(dirname "$PROJECT_DIR")/UnityBackend" ;; esac
  fi
  if [[ -d "$BACKEND_DIR/.git" && ! -f "$BACKEND_DIR/pyproject.toml" ]]; then
    log "Updating the old UnityBackend checkout in $BACKEND_DIR (needs the pyproject.toml layout)"
    git -C "$BACKEND_DIR" pull -q --ff-only
  elif [[ ! -d "$BACKEND_DIR" ]]; then
    log "Cloning UnityBackend into $BACKEND_DIR"
    git clone -q "$BACKEND_REPO" "$BACKEND_DIR"
  fi
  [[ -f "$BACKEND_DIR/pyproject.toml" ]] || die "$BACKEND_DIR has no pyproject.toml"
  log "Setting up UnityBackend venv in $BACKEND_DIR/.venv"
  [[ -x "$BACKEND_DIR/.venv/bin/python" ]] || python3 -m venv "$BACKEND_DIR/.venv"
  "$BACKEND_DIR/.venv/bin/pip" install -q --upgrade pip
  "$BACKEND_DIR/.venv/bin/pip" install -q -e "$BACKEND_DIR[dev]"
  echo "export BACKEND_DIR=\"$BACKEND_DIR\"" >>/etc/profile.d/project-beta-unity.sh
fi

log "Done. Editor: $UNITY_BIN"
"$UNITY_BIN" -version 2>/dev/null || true
