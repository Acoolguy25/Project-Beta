#!/bin/bash
# waitphase.sh PHASE [tries] : polls the server until the night is in PHASE.
D="$(dirname "$0")"
for i in $(seq 1 ${2:-90}); do r=$(T=10 "$D/srv.sh" -e 'return UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>().Phase.ToString();' | grep -o '"result": "[A-Za-z]*"'); echo "$r" | grep -q "\"$1\"" && { echo "phase $1"; exit 0; }; sleep 1; done; echo "timeout ($r)"; exit 1
