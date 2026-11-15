#!/usr/bin/env bash
# Activate a Unity license for this session. Run after setup.sh, once per new container.
# Environment setup scripts cannot read the environment's secrets, so licensing is a separate step.
#
# It uses the first of these that is available:
#   UNITY_LICENSE_SERIAL         Plus/Pro serial            -> unity license activate (serial kept off argv)
#   UNITY_LICENSE_B64            base64 of a Unity_lic.ulf  -> unity license activate --file
#   UNITY_LICENSE_FILE           path to a .ulf             -> unity license activate --file
#   (none)                       interactive: unity auth login (prints a sign-in URL any browser can
#                                open), then, with UNITY_ACCEPT_PERSONAL_EULA=1, activates Personal.
#
# UNITY_ACCEPT_PERSONAL_EULA=1 means the account owner accepted the Unity Personal terms
# (https://unity.com/legal/terms-of-service). Never set it on someone else's behalf.
# UNITY_ACCEPT_EDITOR_TERMS=1 means the account owner accepted the Unity Editor Software Terms
# (https://unity.com/legal/editor-terms-of-service/software) that Unity 6.6+ asks for once per
# Editor version; without it a graphical Editor stops on a terms dialog.
set -euo pipefail
export UNITY_NO_CONSENT_PROMPT=1 UNITY_NO_AGENT_HINTS=1
U=(unity --no-banner --non-interactive)

log() { printf '[license] %s\n' "$*"; }

[[ -f /etc/profile.d/project-beta-unity.sh ]] && source /etc/profile.d/project-beta-unity.sh
if "${U[@]}" license status >/dev/null 2>&1; then
  log "A license is already active."
  if [[ -n "${UNITY_VERSION:-}" && "${UNITY_ACCEPT_EDITOR_TERMS:-0}" == 1 ]]; then
    "${U[@]}" terms accept "$UNITY_VERSION" --yes
  fi
  exit 0
fi

if [[ -n "${UNITY_LICENSE_SERIAL:-}" ]]; then
  log "Activating serial from UNITY_LICENSE_SERIAL"
  "${U[@]}" license activate
elif [[ -n "${UNITY_LICENSE_B64:-}" || -n "${UNITY_LICENSE_FILE:-}" ]]; then
  f="${UNITY_LICENSE_FILE:-}"
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
"${U[@]}" license status

if [[ -n "${UNITY_VERSION:-}" && "${UNITY_ACCEPT_EDITOR_TERMS:-0}" == 1 ]]; then
  log "Accepting Unity Editor Software Terms for $UNITY_VERSION"
  "${U[@]}" terms accept "$UNITY_VERSION" --yes
fi
