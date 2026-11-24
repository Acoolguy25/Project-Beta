#!/bin/bash
# start.sh : the server enters play (ServerInit), then the client joins; returns once a night has begun.
source "$(dirname "$0")/env.sh"; D="$(dirname "$0")"; L="$OUT/server.log"; n0=$(grep -c "begins:" "$L")
"$D/srv.sh" -e 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/ServerInit.unity"); UnityEditor.EditorApplication.EnterPlaymode(); return "entering";'
for i in $(seq 1 40); do T=10 "$D/srv.sh" -e 'return UnityEngine.SceneManagement.SceneManager.GetSceneByName("classic_horror_start").isLoaded.ToString();' | grep -q True && break; sleep 4; done
"$D/srv.sh" -e 'Application.targetFrameRate = 30; return "fps cap";' > /dev/null
"$D/cli.sh" -e 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity"); Application.runInBackground = true; UnityEditor.EditorApplication.EnterPlaymode(); return "play";'
for i in $(seq 1 400); do [ $(grep -c "begins:" "$L") -gt $n0 ] && break; sleep 0.5; done; grep "begins:" "$L" | tail -1
