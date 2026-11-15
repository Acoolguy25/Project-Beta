# Cloud dev environment (Unity + ParrelSync)

Scripts that let a Linux cloud container (for example a Claude Code cloud session) install the
exact Unity Editor this project uses and run the game locally as **server + client** through a
ParrelSync clone. UnityBackend isn't needed for this; it's optional.

| Script | What it does | When |
|---|---|---|
| `setup.sh` | System libraries, Xvfb, the Unity Editor plus Linux Server, Windows Server and Windows Mono modules (version and changeset read from `ProjectSettings/ProjectVersion.txt`, md5-checked against Unity's release manifest), Git LFS assets, the Unity CLI (sha256-checked), and optionally the UnityBackend venv | Once per container (works as the environment's **Setup script**) |
| `license.sh` | Activates a Unity license and, if the account owner agreed, accepts the Editor terms | Once per new container |
| `local-multiplayer.sh` | Imports the project, creates the ParrelSync clone, starts a server Editor and a client Editor under Xvfb, enters Play mode, takes screenshots | Whenever you want a local client/server run |
| `backend.sh` | Runs UnityBackend locally in debug mode on `127.0.0.1:8212` (`start`, `stop`, `status`, `log`) | Used by `local-multiplayer.sh` with `BACKEND=1`, or on its own |

## Quick start

```bash
sudo Tools/CloudEnv/setup.sh                 # sudo INSTALL_BACKEND=1 ... to also clone UnityBackend + venv
source /etc/profile.d/project-beta-unity.sh
Tools/CloudEnv/license.sh                    # see "License" below
Tools/CloudEnv/local-multiplayer.sh          # import -> clone -> start -> play -> shot
Tools/CloudEnv/local-multiplayer.sh shot     # more screenshots, in Logs/local-multiplayer/
Tools/CloudEnv/local-multiplayer.sh stop
```

### With UnityBackend

```bash
sudo INSTALL_BACKEND=1 Tools/CloudEnv/setup.sh   # clones https://github.com/Acoolguy25/UnityBackend.git to ../UnityBackend if missing
BACKEND=1 Tools/CloudEnv/local-multiplayer.sh    # backend + server + client; the client joins through the backend
BACKEND=1 Tools/CloudEnv/local-multiplayer.sh stop
```

With `BACKEND=1` the Editors run with `PROJECT_BETA_DIRECT_CONNECTION=0`, which overrides
`editor_direct_connection` in `LocalNetworkConfig.asset` for that process only (Editor-only code in
`NetworkSettings.cs`; `1` forces direct connection, unset uses the asset). So:

- `backend.sh` starts UnityBackend the same way its `run.sh` does (`python -m net`) but forces its
  **debug** mode, which it normally only picks on a machine whose hostname contains "ryan". Prod
  mode would demand real Unity player tokens and try to launch `LinuxServer` builds. In debug mode it
  listens on `127.0.0.1:8212`, which `LocalNetworkConfig`/`ServerNetworkConfig` already point at,
  and hands every player the server Editor as `unity-test-server` on port 20000.
- It runs from `~/.cache/project-beta-backend` with a copy of `game.db`, so the UnityBackend checkout
  stays clean (`BACKEND_RESET=1` re-copies the database). It writes
  `LinuxServer/application.json` there from the Linux Server profile's `bundleVersion` unless the
  checkout has one from a server build.
- The server Editor registers with the backend over its websocket. The client logs in (`no_login`),
  and the main menu's EasyDebug `DebugPlayGame` asks the backend to play `war_valley`; the backend
  hands back `unity-test-server` on `127.0.0.1:20000`, and the server adds the player through the
  backend's internal API. The checkout's `game.db` has no `war_valley` row, so `backend.sh` adds
  `BACKEND_UNIVERSES` (default `war_valley`) to its local copy.
- UnityBackend's `requirements.txt` isn't pinned. FastAPI 0.145 removed the `**extra` arguments the
  backend passes from `root.toml` (`docs = ""`), so a fresh install fails with "unexpected keyword
  argument 'docs'". `setup.sh` installs with `backend-constraints.txt` (`fastapi<0.145`). A fresh
  install on the VPS would hit the same error.

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

Never hard-coded. `setup.sh` parses `m_EditorVersionWithRevision` (for example `6000.6.9f1
(8c45277fb9c5)`) and downloads from:

- `https://download.unity3d.com/download_unity/<changeset>/unity-<version>-linux.ini` (manifest with md5s)
- `https://download.unity3d.com/download_unity/<changeset>/LinuxEditorInstaller/Unity.tar.xz`
- `https://download.unity3d.com/download_unity/<changeset>/LinuxEditorTargetInstaller/UnitySetup-Linux-Server-Support-for-Editor-<version>.tar.xz`
- `https://download.unity3d.com/download_unity/<changeset>/MacEditorTargetInstaller/UnitySetup-Windows-{Server,Mono}-Support-for-Editor-<version>.pkg`
  (Unity's Linux manifest points at these `.pkg` files; Unity Hub on Linux unpacks the same ones)

Why the Windows modules: some asmdefs exclude `WindowsStandalone64Server`, and the Editor refuses to
compile when that platform isn't installed. The client Editor uses the `Windows` build profile, like
your Windows machine, so its `bundleVersion` matches the server profile's (see Known issues).

When the project is upgraded, re-running `setup.sh` installs the new version next to the old one
under `/opt/unity/editors/<version>`.

## License

Unity can't open a project without one. A Personal license is a file
(`~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml`) bound to `/etc/machine-id`, which
the cloud containers share, and it works without being signed in. So `license.sh` first restores the
copy saved in project files (`/mnt/project-files/project-beta/cloud-env/license/`, override with
`UNITY_LICENSE_STORE`) and only falls back to activating when that doesn't give an active license.
After activating it saves the file there. `license.sh save` / `license.sh restore` do just one half.

Otherwise it uses the first of these that's set:

1. `UNITY_LICENSE_SERIAL`: a Plus/Pro serial (env var in the cloud environment settings).
2. `UNITY_LICENSE_B64` (base64 of a `Unity_lic.ulf`) or `UNITY_LICENSE_FILE`.
3. Otherwise it runs `unity auth login`, which prints a Unity sign-in URL that works from any
   browser (it isn't a localhost callback). With `UNITY_ACCEPT_PERSONAL_EULA=1` it then activates
   Unity Personal. Only set that if the account owner accepted the Personal terms.

Unity 6.6+ also shows a blocking **Unity Editor Software Terms** dialog once per Editor version.
`license.sh` accepts them when the account owner has: `UNITY_ACCEPT_EDITOR_TERMS=1` (for example in
the cloud environment's variables, which covers every version), or a recorded acceptance for that
exact version in `<store>/editor-terms-accepted/<version>`, which `license.sh save` writes once the
terms are accepted.

`setup.sh` points the Unity CLI at the Editor's bundled licensing client
(`UNITY_CLI_INTERNAL_LICENSING_CLIENT_PATH`), because otherwise the CLI downloads one from
`public-stevedore.unity3d.com`, which some network policies block. `local-multiplayer.sh start` stops with a clear
message instead of hanging on that dialog.

## How the local client/server run works

- `Assets/Resources/NetworkSettings/LocalNetworkConfig.asset` has `editor_direct_connection: 1`,
  `editor_universe_id` (currently `war_valley`), `editor_game_port: 20000`. In that mode the server binds
  `127.0.0.1:20000`, `ClientConnector` joins it directly, and backend calls are disabled.
- The **server** is the ParrelSync clone `../Project-Beta_clone_0` (created with ParrelSync's own
  `ClonesManager.CreateCloneFromCurrent`, argument `server`), switched to the
  `Linux Server` build profile (so `UNITY_SERVER` is defined) and playing `Assets/Scenes/ServerInit.unity`.
- The **client** is the main project, in the `Windows` build profile, playing `Assets/Scenes/MainMenu.unity`.
- Each Editor runs on its own Xvfb display (`:91` server, `:92` client) so screenshots are separate.
  Logs and screenshots go to `Logs/local-multiplayer/` (ignored by git).
- Graphical Editors started as root show a "running Unity as root" dialog; the script clicks
  "Continue anyway" with `xdotool`.
- Editors are driven with the Unity CLI (`unity status`, `unity command open_scene`,
  `editor_play`, `editor_stop`, `eval`) through the `com.unity.pipeline` package in the manifest.

ParrelSync comes from `Packages/manifest.json`:
`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync` (locked to commit
`610157ad762084380380148ba8ce14e266a6da97` in `packages-lock.json`).

## Things the Linux Editor changes that you shouldn't commit

Opening the project on Linux rewrites `Packages/manifest.json` and `packages-lock.json` (adds
`com.unity.toolchain.linux-x86_64-linux`, bumps the sdk packages), deletes
`Packages/com.ryan.codegenerator/RpcGenerator.csproj.meta`, and migrates
`ProjectSettings/Packages/com.unity.dedicated-server/ContentSelectionSettings.asset`. Discard those
before committing: `git checkout -- Packages ProjectSettings/Packages`.

## Known issues found while setting this up

- `ProjectSettings` has `bundleVersion: 0.1.0`, but both build profiles override it to `0.04e`. The
  server rejects a client whose version differs, so any Editor without a build profile active
  can't join.
- `*.asset` is marked `unity-yaml` (`eol=lf`) in `.gitattributes`, but some assets are saved in
  binary, e.g. `war_valley/start_map/WarValleyTerrain.asset` and
  `war_valley/war_valley_start/NavMesh-Navigation.asset`. Line-ending normalization damages them in
  git, so a fresh clone logs "Unknown error occurred while loading" for both. Mark them `binary` in
  `.gitattributes` (like the existing `LightingData.asset` rule) and re-commit them from a working
  copy that still has the good files.
- Containers here boot with IPv6 disabled, but Unity's Mono still reports
  `Socket.OSSupportsIPv6 = true`. LiteNetLib crashed creating its IPv6 socket (`NetManager.Socket.cs`
  now falls back to IPv4), and every `HttpClient` request failed with "An address incompatible with
  the requested protocol was used" because `SocketsHttpHandler` always opens a dual-stack socket.
  `Assets/RyanAssets/NetworkService/IPv6Support.cs` checks once at startup and, if an IPv6 socket
  can't be created, clears Mono's cached flag so requests go over IPv4. (Mono ignores the
  `DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER` switch, so there's no environment-only fix.)

## Resources

The Editor is ~9 GB installed, the main project's `Library/` and the clone's copy add several GB
each. Two Editors fit in 16 GB RAM, and the first import is the slow part.
