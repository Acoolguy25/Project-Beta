#!/bin/bash
# corner_chase.sh : the try-9 sight-break run. An investigator 21 m from Father Crane sprints round the chapel-side
# houses and climbs into the cabinet he did not see them reach; the server trace shows what he does next.
# Run during Act I with the client's cameras switched off (see README.md), after the night has released him.
D="$(dirname "$0")"; source "$D/env.sh"; P="$D/probes"
"$D/srv.sh" "$P/srv_lives.cs"
"$D/tpl.sh" cli "$P/cli_tp.tpl" PX=530f PY=16.85f PZ=451f YAW=90; sleep 3
"$D/tpl.sh" srv "$P/srv_trace.tpl" LOG="$OUT/corner_server.log"
"$D/tpl.sh" cli "$P/cli_run.tpl" GX=572.96f GY=17.5f GZ=451.91f AGAIN=60 DROPAT=0 WALKAFTER=0 LOG="$OUT/corner_bot.log"
for i in $(seq 1 30); do [ "$(grep -c pos "$OUT/corner_bot.log")" -ge 2 ] && break; sleep 0.3; done
"$D/tpl.sh" srv "$P/srv_hunt.tpl" PX=513.7f PY=16.8f PZ=451f
for i in $(seq 1 80); do grep -q END "$OUT/corner_bot.log" && break; sleep 0.25; done
"$D/tpl.sh" srv "$P/srv_hide.tpl" DELAY=0
sleep 40; "$D/tpl.sh" srv "$P/srv_trace.tpl" LOG=stop
