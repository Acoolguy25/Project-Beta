#!/usr/bin/env bash
# Activate a Unity license for this session. Run after setup.sh, once per new container.
# Environment setup scripts cannot read the environment's variables or secrets, so licensing is a
# separate step.
#
# Usage: Tools/CloudEnv/license.sh [activate|save|restore]   (default: activate)
#   activate  restore a saved license if there is one, else activate (below), then save it
#   save      copy the active license (and this Editor version's terms acceptance) to the store
#   restore   only restore from the store; exit 3 if that doesn't give an active license
#
# A Unity Personal license is a file (UnityEntitlementLicense.xml) bound to the machine id in
# /etc/machine-id, and it keeps working without being signed in. So once it's activated, it's
# saved to $UNITY_LICENSE_STORE (default /mnt/project-files/project-beta/cloud-env/license, shared
# by the project's sessions) and later sessions restore it instead of signing in again. The cloud
# containers share one machine id; a file bound to another id isn't restored, and it falls through
# to sign-in.
#
# Activation, when nothing could be restored, uses the first of these that is available:
#   UNITY_LICENSE_SERIAL         Plus/Pro serial            -> unity license activate (serial kept off argv)
#   UNITY_LICENSE_B64            base64 of a Unity_lic.ulf  -> unity license activate --file
#   UNITY_LICENSE_FILE           path to a .ulf             -> unity license activate --file
#   (none)                       interactive: unity auth login (prints a sign-in URL any browser can
#                                open), then, with UNITY_ACCEPT_PERSONAL_EULA=1, activates Personal.
#
# UNITY_ACCEPT_PERSONAL_EULA=1 means the account owner accepted the Unity Personal terms
# (https://unity.com/legal/terms-of-service). Never set it on someone else's behalf.
#
# Unity Editor Software Terms (https://unity.com/legal/editor-terms-of-service/software): Unity 6.6+
# asks for them once per Editor version, and a graphical Editor stops on a dialog until they're
# accepted. They're accepted here when UNITY_ACCEPT_EDITOR_TERMS=1 (the account owner's standing
# acceptance, e.g. set in the cloud environment's variables), or when the store records that the
# owner already accepted them for this exact Editor version. A new Editor version is never covered
# by an earlier acceptance.
set -euo pipefail
export UNITY_NO_CONSENT_PROMPT=1 UNITY_NO_AGENT_HINTS=1
U=(unity --no-banner --non-interactive)

log() { printf '[license] %s\n' "$*"; }

[[ -f /etc/profile.d/project-beta-unity.sh ]] && source /etc/profile.d/project-beta-unity.sh
STORE="${UNITY_LICENSE_STORE:-/mnt/project-files/project-beta/cloud-env/license}"
LIC_DIR="$HOME/.config/unity3d/Unity/licenses"
LIC_FILE="UnityEntitlementLicense.xml"
MACHINE_ID="$(cat /etc/machine-id 2>/dev/null || echo unknown)"

active() { "${U[@]}" license status >/dev/null 2>&1; }
store_ok() { [[ -d "$(dirname "$STORE")" ]]; }

terms_status() { # -> accepted | not-accepted | no-terms-file | (empty if unknown)
  [[ -n "${UNITY_VERSION:-}" ]] || return 0
  "${U[@]}" terms status --format tsv --no-header 2>/dev/null | awk -F'\t' -v v="$UNITY_VERSION" '$1==v{print $3}'
}

cmd_save() {
  store_ok || { log "no store at $(dirname "$STORE"); not saving"; return 0; }
  mkdir -p "$STORE"
  if [[ -f "$LIC_DIR/$LIC_FILE" ]] && active; then
    cp "$LIC_DIR/$LIC_FILE" "$STORE/$LIC_FILE"
    log "saved license for machine $MACHINE_ID to $STORE"
  fi
  if [[ "$(terms_status)" == accepted ]]; then
    mkdir -p "$STORE/editor-terms-accepted"
    date -u +%FT%TZ >"$STORE/editor-terms-accepted/$UNITY_VERSION"
    log "recorded Editor terms acceptance for $UNITY_VERSION"
  fi
}

restore_terms() {
  [[ "$(terms_status)" == not-accepted ]] || return 0
  if [[ "${UNITY_ACCEPT_EDITOR_TERMS:-0}" == 1 || -f "$STORE/editor-terms-accepted/$UNITY_VERSION" ]]; then
    log "Accepting Unity Editor Software Terms for $UNITY_VERSION (accepted by the account owner)"
    "${U[@]}" terms accept "$UNITY_VERSION" --yes >/dev/null
  else
    log "Unity Editor Software Terms for $UNITY_VERSION are not accepted yet. If the account owner"
    log "accepts them: UNITY_ACCEPT_EDITOR_TERMS=1 $0   (recorded for later sessions)"
  fi
}

cmd_restore() {
  if ! active && [[ -f "$STORE/$LIC_FILE" ]]; then
    if grep -q "Id=\"$MACHINE_ID\"" "$STORE/$LIC_FILE"; then
      log "restoring saved license from $STORE"
      mkdir -p "$LIC_DIR"
      cp "$STORE/$LIC_FILE" "$LIC_DIR/$LIC_FILE"
    else
      log "saved license is bound to another machine id, not $MACHINE_ID; not restoring it"
    fi
  fi
  restore_terms
  active
}

activate() {
  if [[ -n "${UNITY_LICENSE_SERIAL:-}" ]]; then
    log "Activating serial from UNITY_LICENSE_SERIAL"
    "${U[@]}" license activate
  elif [[ -n "${UNITY_LICENSE_B64:-}" || -n "${UNITY_LICENSE_FILE:-}" ]]; then
    local f="${UNITY_LICENSE_FILE:-}"
    if [[ -z "$f" ]]; then
      f="$(mktemp --suffix=.ulf)"; trap 'rm -f "$f"' EXIT
      base64 -d <<<"$UNITY_LICENSE_B64" >"$f"
    fi
    log "Activating license file"
    "${U[@]}" license activate --file "$f"
  else
    if ! "${U[@]}" auth status >/dev/null 2>&1; then
      log "Not signed in. Open the auth_url printed below in any browser and sign in."
      unity --no-banner auth login --insecure-storage
    fi
    if [[ "${UNITY_ACCEPT_PERSONAL_EULA:-0}" == 1 ]]; then
      log "Activating Unity Personal"
      "${U[@]}" license activate --personal --accept-eula
    else
      log "Signed in. Activate with: unity license activate   (assigned seat)"
      log "or, once the account owner accepts the Personal terms: UNITY_ACCEPT_PERSONAL_EULA=1 $0"
      exit 4
    fi
  fi
}

case "${1:-activate}" in
  save) cmd_save ;;
  restore) cmd_restore || { log "no usable saved license for machine $MACHINE_ID"; exit 3; } ;;
  activate)
    if cmd_restore; then
      log "A license is active."
    else
      activate
      restore_terms
    fi
    "${U[@]}" license status
    cmd_save
    ;;
  *) log "unknown command '$1'"; exit 2 ;;
esac
