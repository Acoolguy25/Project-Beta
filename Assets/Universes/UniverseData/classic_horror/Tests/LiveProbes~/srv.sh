#!/bin/bash
# srv.sh <file.cs | -e code> : evaluates C# in the server Editor.
source "$(dirname "$0")/env.sh"
if [ "$1" = "-e" ]; then code="$2"; else code="$(cat "$1")"; fi
unity command --project-path "$SERVER_PROJECT" --timeout ${T:-60} --result-only script eval "$code" 2>&1
