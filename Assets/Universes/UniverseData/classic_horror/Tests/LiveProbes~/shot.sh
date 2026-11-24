#!/bin/bash
# shot.sh name : grabs the client's Game view (Editor window at 1600x900, top left) to $OUT/name.png.
source "$(dirname "$0")/env.sh"
DISPLAY=$CLIENT_DISPLAY import -window root -crop 1600x770+0+106 "$OUT/$1.png" && echo "$OUT/$1.png"
