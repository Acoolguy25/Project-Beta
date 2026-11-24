#!/bin/bash
# tpl.sh srv|cli probes/file.tpl KEY=VALUE... : fills a template's KEYs and evaluates it in that Editor.
who=$1; f=$2; shift 2; code="$(cat "$f")"; for kv in "$@"; do code="${code//${kv%%=*}/${kv#*=}}"; done
"$(dirname "$0")/$who.sh" -e "$code"
