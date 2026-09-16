# Compose Unity Docker Image

This repository builds Linux and Windows Docker images for installing Unity editors and running Unity project builds and tests through [slothsoft/unity](https://github.com/Faulo/slothsoft-unity).

## Image Contents

### Common

Both image variants provide:

- Unity Hub 3.12.1.
- The native `compose-unity` launcher, backed by Composer 2 and
  `slothsoft/unity`.
- A long-running sidecar with health checks and process-tree cleanup.
- An optional ASP.NET Core Streamable HTTP MCP server for inspecting projects,
  running tests, executing editor methods, and building and serving WebGL.
- Node.js and npm.
- The itch.io Butler client.
- SteamCMD.
- Blender 4.5.
- PHP (8.4 on Linux and 8.2 on Windows).
- .NET SDK 9 and DocFX.
- Git and Git LFS.
- Python 3.
- FFmpeg, curl, and archive utilities.

The following build arguments are shared by both variants:

| Argument | Default | Purpose |
| --- | --- | --- |
| `BLENDER_SERIES` | `4.5` | Selects the Blender release series. |
| `DOTNET_VERSION` | `9.0` | Selects the .NET SDK feature version used by the image and native launcher. |
| `NODE_VERSION` | `24` | Selects the installed Node.js major release. |
| `SLOTHSOFT_UNITY_VERSION` | `2.22` | Selects the compatible `slothsoft/unity` release. |
| `UNITY_TIMEOUT` | `14400` | Sets Composer's process timeout in seconds for Unity commands. |

### Linux

The Linux image is based on Debian Trixie Slim and additionally provides:

- Xvfb and X authentication tools for headless editor execution.
- ICU for the .NET-based Unity Licensing Client.
- OpenSSL 1.1 compatibility for Unity 2021.

Linux-specific build arguments are:

| Argument | Default | Purpose |
| --- | --- | --- |
| `BOOKWORM_SNAPSHOT` | `20260615T025018Z` | Selects the archived Bookworm snapshot that supplies legacy editor libraries removed in Trixie. |
| `BULLSEYE_SECURITY_SNAPSHOT` | `20260615T025018Z` | Selects the archived Bullseye Security snapshot that supplies OpenSSL 1.1 for Unity 2021. |
| `DEBIAN_FRONTEND` | `noninteractive` | Keeps Debian package installation non-interactive during the build. |

### Windows

The Windows image uses the full Windows container base so Unity packages can
access desktop WinRT APIs. It additionally provides:

- Chocolatey.
- The latest stable PowerShell 7 release as `pwsh`.
- Visual Studio 2019 Build Tools and .NET Framework 4.7.1 reference
  assemblies.
- Visual C++ 2010, 2013, and 2015-2022 runtimes required by Unity's native
  editor libraries.
- Container-safe `.blend` and `.fbx` associations that invoke `blender.exe`
  directly, including a `SHELL32` compatibility proxy for mounted paths.

Windows-specific build arguments are:

| Argument | Default | Purpose |
| --- | --- | --- |
| `OS_BASE` | `ltsc2019` | Selects the supported Windows LTSC 2019 base. Legacy `20H2` build inputs are retained as implementation reference only. |
| `POWERSHELL_MAJOR` | `7` | Selects the stable PowerShell major release line. |

## Repository Layout

Build inputs are organized by ownership:

- `common/ComposeUnity/` contains the shared cross-platform launcher and
  sidecar source, targeting .NET 9.0 by default.
- `common/ComposeUnity.Tests/` contains daemon-free unit tests and their Unity
  project fixtures.
- `common/ComposeUnity.DaemonTests/` contains opt-in NUnit tests against local
  Linux and Windows Docker daemons and a deterministic worker backend.
- `linux/` contains the Linux Dockerfile and machine identity.
- `windows/` contains the Windows Dockerfile, the `UnityHub` and `SteamCmd` launcher projects, the Hub patch, Chocolatey package, and PHP extension configuration.

Both Docker builds use the repository root as build context. Both images create their Composer project during the build, require `slothsoft/unity`, and configure the process timeout from `UNITY_TIMEOUT`.

The compose-unity tool stack is installed at `/compose-unity` in the Linux
image and `C:\compose-unity` in the Windows image. Runtime-owned subdirectories,
including the WebGL document root, live beneath these locations.

## Docker Contexts

The local development setup assumes two explicitly named Docker contexts:

- `linux` should point to a Docker host running Linux containers.
- `windows` should point to a Docker host running Windows containers.

## GPU Acceleration

Both images default to `UNITY_NO_GRAPHICS=1` so they remain usable on GPU-less
hosts. Expose a compatible host GPU and set `UNITY_NO_GRAPHICS=0` to allow
Unity to initialize a graphics device:

On Windows:

```yaml
services:
  compose-unity:
    image: faulo/compose-unity:latest
    environment:
      UNITY_NO_GRAPHICS: 0
    isolation: process
    devices:
      - "class/5B45201D-F2F2-4F3B-85BB-30FF1F953599"
```

On Linux:

```yaml
services:
  compose-unity:
    image: faulo/compose-unity:latest
    environment:
      UNITY_NO_GRAPHICS: 0
    gpus: all
```

See the platform documentation for
[Windows container GPU acceleration](https://learn.microsoft.com/en-us/virtualization/windowscontainers/deploy-containers/gpu-acceleration),
[Docker GPU access](https://docs.docker.com/engine/containers/gpu/), and the
[Compose `gpus` attribute](https://docs.docker.com/reference/compose-file/services/#gpus).

## Volumes and Licensing

Use the following named volumes to persist Unity editors, configuration, Hub
state, caches, and licensing data on Linux:

```yaml
volumes:
  - steam:/root/Steam
  - unity-binaries:/root/Unity
  - unity-config:/root/.config/unity3d
  - unity-hub:/root/.config/unityhub
  - unity-cache:/root/.cache/Unity
  - unity-license:/root/.local/share/unity3d
```

The equivalent Windows volume mappings are:

```yaml
volumes:
  - steam:C:/steam
  - unity-binaries:C:/Program Files/Unity/Hub/Editor
  - unity-config:C:/Users/ContainerAdministrator/AppData/Roaming/Unity
  - unity-hub:C:/Users/ContainerAdministrator/AppData/Roaming/UnityHub
  - unity-cache:C:/Users/ContainerAdministrator/AppData/Local/Unity
  - unity-license:C:/ProgramData/Unity
```

`linux/machine-id` supplies a stable Linux machine identity used by the image. Changes to that file, credential forwarding, editor paths, or licensing volumes can invalidate persisted licensing and should be made deliberately.

## Cross-Platform Command

Both images expose the same `compose-unity` command, shown here with one of the
pinned test versions:

```text
compose-unity exec unity-empty-project test 2021.3.45f2
```

Both images compile the shared C# source in `common/ComposeUnity/` into one
canonical native launcher named `compose-unity`. The launcher invokes Composer
without an intermediate shell, selects the installed
`/compose-unity/composer.json` or `C:\compose-unity\composer.json` through
Composer's `COMPOSER` environment variable, pins installed dependencies to that
same directory, preserves the caller's working directory, attached input,
output, and exit status, and registers each call with the sidecar. The
image-level `COMPOSE_UNITY` command points to this native launcher as well.

### Runtime credentials

Unity, mailbox, and Steam credentials accept either their existing direct
environment variables or conventional file-backed variants:

| Credential | Direct variables | File-backed variables |
| --- | --- | --- |
| Unity | `UNITY_CREDENTIALS_USR`, `UNITY_CREDENTIALS_PSW` | `UNITY_CREDENTIALS_USR_FILE`, `UNITY_CREDENTIALS_PSW_FILE` |
| Mailbox | `EMAIL_CREDENTIALS_USR`, `EMAIL_CREDENTIALS_PSW` | `EMAIL_CREDENTIALS_USR_FILE`, `EMAIL_CREDENTIALS_PSW_FILE` |
| Steam | `STEAM_CREDENTIALS_USR`, `STEAM_CREDENTIALS_PSW` | `STEAM_CREDENTIALS_USR_FILE`, `STEAM_CREDENTIALS_PSW_FILE` |

Each username/password pair may mix direct and file-backed inputs, but the
direct and `_FILE` forms of one value cannot both be set. Configured pairs must
be complete. Missing, unreadable, and empty files fail before the requested
command starts. A trailing line ending written by a secrets manager is removed;
other whitespace remains part of the credential.

The launcher resolves file-backed values only for the child command. MCP passes
resolved Unity and mailbox credentials to each worker command without storing
them in retained worker container configuration or labels. Steam credentials
are used by the one-off `steam-login` command.

## Long-Running Sidecar

With no explicit command, the image starts `compose-unity sidecar` as PID 1.
That sidecar can be utilized by the `withUnity` command of [jenkins-unity](https://github.com/Faulo/jenkins-unity).

## Jenkins Docker Pipeline

Both images support the unmodified Jenkins Docker Pipeline `inside` form:

```groovy
docker.image(image).inside {
    sh 'something'
}
```

The plugin's `cat` keeper on Linux and `cmd.exe` keeper on Windows replace the
default sidecar command, and subsequent Pipeline steps run through `docker
exec`. Other explicitly supplied Docker commands likewise replace the complete
default invocation; use `compose-unity` explicitly when invoking the image
launcher this way.

## Optional MCP Server

Both images default `COMPOSE_UNITY_MCP` to `0`. Set it to `1` to run the sidecar
supervisor and an official ASP.NET Core Streamable HTTP MCP server together.
The endpoint is fixed at `/mcp` on container port `8080`.

The MCP sidecar controls persistent Unity worker containers, so it must be able
to access the same local Docker Engine that runs it. Publish the endpoint only
on host loopback. A Linux Compose service can be configured as follows:

```yaml
services:
  unity:
    image: faulo/compose-unity:latest
    environment:
      - COMPOSE_UNITY_MCP=1
      - UNITY_CREDENTIALS_USR_FILE=/run/secrets/unity-user
      - UNITY_CREDENTIALS_PSW_FILE=/run/secrets/unity-password
      - EMAIL_CREDENTIALS_USR_FILE=/run/secrets/email-user
      - EMAIL_CREDENTIALS_PSW_FILE=/run/secrets/email-password
      - STEAM_CREDENTIALS_USR_FILE=/run/secrets/steam-user
      - STEAM_CREDENTIALS_PSW_FILE=/run/secrets/steam-password
    ports:
      - "127.0.0.1:3310:8080"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - unity-binaries:/root/Unity
      - unity-config:/root/.config/unity3d
      - unity-hub:/root/.config/unityhub
      - unity-cache:/root/.cache/Unity
      - unity-license:/root/.local/share/unity3d
    tmpfs:
      - /compose-unity/webgl
    secrets:
      - unity-user
      - unity-password
      - email-user
      - email-password
      - steam-user
      - steam-password

volumes:
  unity-binaries:
  unity-config:
  unity-hub:
  unity-cache:
  unity-license:

secrets:
  unity-user:
    file: ./secrets/unity-user.txt
  unity-password:
    file: ./secrets/unity-password.txt
  email-user:
    file: ./secrets/email-user.txt
  email-password:
    file: ./secrets/email-password.txt
  steam-user:
    file: ./secrets/steam-user.txt
  steam-password:
    file: ./secrets/steam-password.txt
```

The equivalent Windows container uses the Docker Engine named pipe and Windows
Unity state paths:

```yaml
services:
  unity:
    image: faulo/compose-unity:latest
    environment:
      - COMPOSE_UNITY_MCP=1
      - 'UNITY_CREDENTIALS_USR_FILE=C:\ProgramData\Docker\secrets\unity-user'
      - 'UNITY_CREDENTIALS_PSW_FILE=C:\ProgramData\Docker\secrets\unity-password'
      - 'EMAIL_CREDENTIALS_USR_FILE=C:\ProgramData\Docker\secrets\email-user'
      - 'EMAIL_CREDENTIALS_PSW_FILE=C:\ProgramData\Docker\secrets\email-password'
      - 'STEAM_CREDENTIALS_USR_FILE=C:\ProgramData\Docker\secrets\steam-user'
      - 'STEAM_CREDENTIALS_PSW_FILE=C:\ProgramData\Docker\secrets\steam-password'
    ports:
      - "127.0.0.1:3310:8080"
    volumes:
      - \\.\pipe\docker_engine:\\.\pipe\docker_engine
      - unity-binaries:C:/Program Files/Unity/Hub/Editor
      - unity-config:C:/Users/ContainerAdministrator/AppData/Roaming/Unity
      - unity-hub:C:/Users/ContainerAdministrator/AppData/Roaming/UnityHub
      - unity-cache:C:/Users/ContainerAdministrator/AppData/Local/Unity
      - unity-license:C:/ProgramData/Unity
    secrets:
      - unity-user
      - unity-password
      - email-user
      - email-password
      - steam-user
      - steam-password

volumes:
  unity-binaries:
  unity-config:
  unity-hub:
  unity-cache:
  unity-license:

secrets:
  unity-user:
    file: ./secrets/unity-user.txt
  unity-password:
    file: ./secrets/unity-password.txt
  email-user:
    file: ./secrets/email-user.txt
  email-password:
    file: ./secrets/email-password.txt
  steam-user:
    file: ./secrets/steam-user.txt
  steam-password:
    file: ./secrets/steam-password.txt
```

For example, Codex MCP configuration uses the same IP and port published by the
container:

```toml
[mcp_servers.compose-unity]
url = "http://127.0.0.1:3310/mcp"
tool_timeout_sec = 7200
```

The server advertises exactly four tools:

Each tool accepts an absolute project path from the Docker host. When a Linux
daemon receives an ordinary Windows drive path such as `C:\Users\name\project`,
the controller probes the standard WSL, current Docker Desktop, and legacy
desktop mount layouts and remembers the last successful layout for the
container lifetime. Native Linux paths and Windows-container daemons keep their
existing path behavior.

`run_tests`, `execute_method`, and `build_and_serve_webgl` can take 30–60
minutes during a cold editor install or first project import. When the caller
supplies `_meta.progressToken`, they emit request-correlated standard MCP
progress notifications for validation, project-lock waits, worker preparation,
the tool-specific operation, and result processing or publication. Quiet phases
send at most one heartbeat per 60 seconds. These notifications indicate
liveness and coarse status; they are not streamed Unity logs or trustworthy
completion percentages. Callers should await the original tool request rather
than restart it or launch parallel diagnostics. `get_project_info` never starts
Unity and does not report progress.

- `get_project_info` validates a Docker-host project path and returns its normalized
  root, company, product and editor versions, major code and rendering settings,
  input handling, and complete package manifest by reading the project's YAML
  and JSON files directly. It never installs or launches Unity. Unity defaults
  that are absent from the serialized project remain explicit as empty override
  maps rather than inferred values. `ProjectSettings/GraphicsSettings.asset`
  is optional; projects without it report the affected rendering settings as
  `Unknown`.
- `run_tests` accepts one or more Unity test modes. A valid JUnit report returns
  `outcome` (`passed` or `failed`), `exitCode`, total/passed/failure/error/skipped
  counts, duration, and up to 100 failure details with complete stack traces.
  `failuresTruncated` reports whether additional failures were omitted. Valid
  reports omit Unity output. If Unity does not produce a trustworthy report,
  the result contains `outcome: "error"`, `exitCode`, and `log`: the complete,
  unfiltered, untruncated stdout/stderr transcript in Docker frame order.
  Stream transitions are marked `[stdout]` and `[stderr]`.
- `execute_method` runs a fully qualified static editor method with
  argument boundaries preserved and asks Unity to quit after it returns.
- `build_and_serve_webgl` installs the selected editor's WebGL Build Support
  module, starts Unity with the WebGL build target selected, invokes
  `Slothsoft.UnityExtensions.Editor.Build.WebGL`, copies the successful build
  from the retained worker through the Docker Engine archive API, and returns a
  browser-ready URL on the MCP listener. The project must provide
  `net.slothsoft.unity-extensions`; the tool does not change package state.

`get_project_info` mounts the project read-only. The other tools mount project
state writable; the controller does not modify those files directly, but Unity
or an executed editor method may change them.

### WebGL document root

The MCP listener exposes an htdocs-style tree at `/webgl/`. Project directories
use a readable slug derived from Unity's product name and contain builds named
with UTC timestamps such as `2026-08-14_18-42-07Z`. Directory browsing is
enabled, while an `index.html` file takes priority as the directory index. A
build URL therefore has this form:

```text
http://127.0.0.1:3310/webgl/example-game/2026-08-14_18-42-07Z/
```

The sidecar document-root paths are:

- Linux: `/compose-unity/webgl`
- Windows: `C:\compose-unity\webgl`

Without a mount, builds live in the sidecar's writable container layer and
disappear with that container. Mount the directory as tmpfs for explicitly
temporary storage.

The server supports Unity's uncompressed, gzip, Brotli, decompression-fallback,
and WebAssembly files with their required MIME types and content encodings. It
also supports streaming, conditional and range requests. Every `/webgl`
response supplies `Cross-Origin-Opener-Policy`,
`Cross-Origin-Embedder-Policy`, and `Cross-Origin-Resource-Policy` so threaded
Unity builds can use `SharedArrayBuffer` on the loopback origin.

MCP startup is included in sidecar readiness, `status`, and Docker health when
enabled. Startup fails if Docker Engine access or self-container inspection is
unavailable. The server accepts loopback `Host` and `Origin` values only; it
does not provide authentication and is not intended for remote publication.

## ComposeUnity Tests

Run the daemon-free test suite with the .NET 9 SDK:

```text
dotnet test common/ComposeUnity.Tests/ComposeUnity.Tests.csproj --configuration Release
```

The suite covers command routing and legacy compatibility, configuration and
sanitization, project probing (including optional graphics settings), Unity
JUnit result parsing, Docker stream framing, state persistence, path handling,
and per-project FIFO behavior.

Real-daemon tests live in a separate project because they build disposable
`tmp/compose-unity-daemon-tests` images and create containers, bind mounts,
execs, and MCP worker state on each available explicitly named local daemon:

```text
dotnet test common/ComposeUnity.DaemonTests/ComposeUnity.DaemonTests.csproj --configuration Release
```

The fixtures use the explicitly named `linux` and `windows` Docker contexts,
verify the daemon-reported container OS, and skip each platform independently
when its context or daemon is unavailable. Only local named-pipe and Unix-socket
contexts are accepted. Set `COMPOSE_UNITY_DAEMON_TEST_REQUIRED_OS` to `linux` or
`windows` to turn an unavailable matching fixture into a test failure. CI sets
this for its platform-specific jobs so a green job proves that its daemon tests
actually ran while ordinary local runs remain portable.

The tests publish the current controller and a deterministic fake
`compose-unity` executable, then assemble a minimal image from the applicable
.NET runtime-deps base. The fake executable occupies the same path workers use
in production and delegates project probing to the real controller, but does
not install Composer, PHP, Unity, or the other production image tools. NUnit
drives Docker and MCP directly and verifies the advertised tools, real project
probing, argument boundaries, Docker output and exit codes, JUnit results,
retained worker reuse, project mounts, and exclusion of the Docker endpoint
from workers. The Windows fixture uses the official .NET 9 Nano Server 1809
image and respects the daemon's default isolation mode, matching fleet behavior.
The GitHub Actions Windows runner configures Hyper-V as the daemon default so
the LTSC 2019 container can run on Windows Server 2022. CI runs the daemon-free
suite plus Linux and Windows daemon fixtures before image builds. Full Unity
invocation remains in the local image test scripts because editor downloads and
licensing make it unsuitable for the lightweight CI harness.
