# Cloud dev environment (Unity + ParrelSync)

Scripts that let a Linux cloud container (for example a Claude Code cloud session) install the
exact Unity Editor this project uses and run the game locally as **server + client** through a
ParrelSync clone. UnityBackend isn't needed for this; it's optional.

| Script | What it does | When |
|---|---|---|
| `setup.sh` | System libraries, Xvfb, Unity Editor + Linux Dedicated Server module (version and changeset read from `ProjectSettings/ProjectVersion.txt`, md5-checked against Unity's release manifest), the Unity CLI (sha256-checked), and optionally the UnityBackend venv | Once per container image (works as the environment's **Setup script**) |
| `license.sh` | Activates a Unity license | Once per new container |
| `local-multiplayer.sh` | Imports the project, creates the ParrelSync clone, starts a server Editor and a client Editor under Xvfb, enters Play mode, takes screenshots | Whenever you want a local client/server run |

## Quick start

```bash
sudo Tools/CloudEnv/setup.sh                 # INSTALL_BACKEND=1 to also set up ../UnityBackend/.venv
source /etc/profile.d/project-beta-unity.sh
Tools/CloudEnv/license.sh                    # see "License" below
Tools/CloudEnv/local-multiplayer.sh          # import -> clone -> start -> play -> shot
Tools/CloudEnv/local-multiplayer.sh shot     # more screenshots, in Logs/local-multiplayer/
Tools/CloudEnv/local-multiplayer.sh stop
```

To use it as the cloud environment's setup script, set the script to:

```bash
#!/bin/bash
set -euo pipefail
if [ -f /home/user/Project-Beta/Tools/CloudEnv/setup.sh ]; then
  bash /home/user/Project-Beta/Tools/CloudEnv/setup.sh
else  # repo not cloned yet: fetch only the two folders the script needs
  git clone -q --depth 1 --filter=blob:none --sparse https://github.com/Acoolguy25/Project-Beta.git /tmp/pb-setup
  git -C /tmp/pb-setup sparse-checkout set Tools/CloudEnv ProjectSettings
  PROJECT_DIR=/tmp/pb-setup bash /tmp/pb-setup/Tools/CloudEnv/setup.sh
fi
```

Setup scripts can't read the environment's secrets, which is why licensing is a separate step.

## Unity version

Never hard-coded. `setup.sh` parses `m_EditorVersionWithRevision` (currently `6000.5.5f1
(d16e074b49fd)`) and downloads from:

- `https://download.unity3d.com/download_unity/<changeset>/unity-<version>-linux.ini` (manifest with md5s)
- `https://download.unity3d.com/download_unity/<changeset>/LinuxEditorInstaller/Unity.tar.xz`
- `https://download.unity3d.com/download_unity/<changeset>/LinuxEditorTargetInstaller/UnitySetup-Linux-Server-Support-for-Editor-<version>.tar.xz`

When the project is upgraded, re-running `setup.sh` installs the new version next to the old one
under `/opt/unity/editors/<version>`.

## License

Unity can't open a project without one. `license.sh` uses the first of these that's set:

1. `UNITY_LICENSE_SERIAL`: a Plus/Pro serial (env var in the cloud environment settings).
2. `UNITY_LICENSE_B64` (base64 of a `Unity_lic.ulf`) or `UNITY_LICENSE_FILE`.
3. Otherwise it runs `unity auth login`, which prints a Unity sign-in URL that works from any
   browser (it isn't a localhost callback). With `UNITY_ACCEPT_PERSONAL_EULA=1` it then activates
   Unity Personal. Only set that if the account owner accepted the Personal terms.

## How the local client/server run works

- `Assets/Resources/NetworkSettings/LocalNetworkConfig.asset` has `editor_direct_connection: 1`,
  `editor_universe_id: dot_invaders`, `editor_game_port: 20000`. In that mode the server binds
  `127.0.0.1:20000`, `ClientConnector` joins it directly, and backend calls are disabled.
- The **server** is the ParrelSync clone `../Project-Beta_clone_0` (created with ParrelSync's own
  `ClonesManager.CreateCloneFromCurrent`, argument `server`), switched to the
  `Linux Server` build profile (so `UNITY_SERVER` is defined) and playing `Assets/Scenes/ServerInit.unity`.
- The **client** is the main project playing `Assets/Scenes/MainMenu.unity`.
- Each Editor runs on its own Xvfb display (`:91` server, `:92` client) so screenshots are separate.
  Logs and screenshots go to `Logs/local-multiplayer/` (ignored by git).
- Editors are driven with the Unity CLI (`unity status`, `unity command script eval`) through the
  `com.unity.pipeline` package that's already in the manifest.

ParrelSync comes from `Packages/manifest.json`:
`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync` (locked to commit
`610157ad762084380380148ba8ce14e266a6da97` in `packages-lock.json`).

## Resources

The Editor is ~9 GB installed, the main project's `Library/` and the clone's copy add several GB
each. Two Editors fit in 16 GB RAM, and the first import is the slow part.
