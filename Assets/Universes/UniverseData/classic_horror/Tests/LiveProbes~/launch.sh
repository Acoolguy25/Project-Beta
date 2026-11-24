#!/bin/bash
# launch.sh : the server Editor (batchmode, Linux Server profile) and the client Editor (the clone, on a virtual display).
source "$(dirname "$0")/env.sh"
nohup "$UNITY_EDITOR" -batchmode -nographics -burst-disable-compilation -projectPath "$SERVER_PROJECT" \
  -activeBuildProfile "Assets/Settings/Build Profiles/Linux Server.asset" -logFile "$OUT/server.log" > /dev/null 2>&1 &
pgrep -f "Xvfb $CLIENT_DISPLAY" > /dev/null || (Xvfb $CLIENT_DISPLAY -screen 0 1600x900x24 -nolisten tcp > /dev/null 2>&1 &)
sleep 2
DISPLAY=$CLIENT_DISPLAY nohup "$UNITY_EDITOR" -projectPath "$CLIENT_PROJECT" -logFile "$OUT/client.log" -screen-width 1600 -screen-height 900 > /dev/null 2>&1 &
echo launched
