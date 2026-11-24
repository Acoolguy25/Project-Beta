#!/bin/bash
# Where things are. Override any of these in the environment before running the scripts.
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_PROJECT="${SERVER_PROJECT:-$(cd "$HERE/../../../../../.." && pwd)}"   # the main project: the server Editor
CLIENT_PROJECT="${CLIENT_PROJECT:-${SERVER_PROJECT}_clone_0}"                  # the ParrelSync clone: the client Editor
OUT="${OUT:-$HERE/out}"                                                       # logs and captures (git-ignored by the ~ folder)
CLIENT_DISPLAY="${CLIENT_DISPLAY:-:92}"
mkdir -p "$OUT"
