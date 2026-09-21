# CI Tools Docker Image

This repository builds Linux x64 and Windows x64 variants of
`faulo/ci-tools`. The image provides standalone command-line tools used by CI
jobs without prescribing a build system or long-running container process.

## Public commands

Both variants provide:

- `butler`, the itch.io upload client.
- `steamcmd`, including persistent bootstrap state.
- `steam-buildfile [ARG...]`, equivalent to
  `composer exec steam-buildfile -- ARG...`.
- `steam-login [ARG...]`, equivalent to
  `composer exec steam-login -- ARG...`.

The Composer-backed commands use `slothsoft/unity` as their implementation
backend. Their native launcher preserves the caller's working directory,
argument boundaries, attached standard streams, termination signals, timeout,
and child exit status.

The image intentionally contains no Unity Hub, Unity editor compatibility
libraries, Unity licensing state, sidecar, MCP server, worker controller,
project probe, or WebGL host. It also excludes Git, Git LFS, Nano, Blender,
Python, FFmpeg, DocFX, and a runtime .NET SDK.

## Image behavior

The images declare no `ENTRYPOINT`, so Docker and Jenkins Pipeline can replace
the complete command. Each image retains its base image's default command:
`bash` on Linux and `cmd.exe` on Windows. Jenkins Docker Pipeline keepers remain
supported with `cat` on Linux and `cmd.exe` on Windows.

The Linux runtime is based on Debian Trixie Slim and uses Trixie's PHP 8.4
release line. Its apt dependencies are declared in `linux/ci-tools.packages`;
PowerShell is not installed.

The Windows runtime is based on
`mcr.microsoft.com/windows/servercore:<OS_BASE>`. Its Chocolatey meta-package
contains Butler, Node.js, PowerShell Core, PHP 8.4, and Composer. SteamCMD
remains a separately downloaded native launcher so it can bootstrap into a
mounted volume. Because PHP 8.4 unbundled IMAP, the Windows image installs its
current official PECL build.

## Build arguments

| Argument | Default | Purpose |
| --- | --- | --- |
| `TOOL_TIMEOUT` | `14400` | Configures Composer's child-process timeout in seconds. |
| `NODE_VERSION` | `24` | Selects the Linux Node.js major release. |
| `OS_BASE` | `ltsc2019` | Selects the Windows Server Core release. |
| `CHOCOLATEY_VERSION` | `1.4.0` | Selects the Windows Chocolatey bootstrap version. |

At runtime, `CI_TOOLS_CALL_TIMEOUT` limits each Composer-backed launcher call
and defaults to 86400 seconds. Set it to `0` to disable the launcher deadline.

## Steam state

Mount the platform Steam state directory to preserve SteamCMD downloads and
authentication state:

Linux:

```yaml
services:
  ci-tools:
    image: faulo/ci-tools:latest
    volumes:
      - steam:/root/Steam

volumes:
  steam:
```

Windows:

```yaml
services:
  ci-tools:
    image: faulo/ci-tools:latest
    volumes:
      - steam:C:/steam

volumes:
  steam:
```

## Runtime credentials

Steam and mailbox credentials accept direct environment variables or
conventional file-backed variants:

| Credential | Direct variables | File-backed variables |
| --- | --- | --- |
| Steam | `STEAM_CREDENTIALS_USR`, `STEAM_CREDENTIALS_PSW` | `STEAM_CREDENTIALS_USR_FILE`, `STEAM_CREDENTIALS_PSW_FILE` |
| Mailbox | `EMAIL_CREDENTIALS_USR`, `EMAIL_CREDENTIALS_PSW` | `EMAIL_CREDENTIALS_USR_FILE`, `EMAIL_CREDENTIALS_PSW_FILE` |

Each username/password pair may mix direct and file-backed inputs, but a direct
variable and its `_FILE` form cannot both be set. Configured pairs must be
complete. Missing, unreadable, and empty files fail before the child command
starts. One trailing line ending is removed from a credential file; other
whitespace is preserved. Resolved values are passed only to the child process.

Example:

```text
docker run --rm \
  -e STEAM_CREDENTIALS_USR \
  -e STEAM_CREDENTIALS_PSW \
  -e EMAIL_CREDENTIALS_USR \
  -e EMAIL_CREDENTIALS_PSW \
  -v steam:/root/Steam \
  faulo/ci-tools:latest steam-login
```

Generate a Steam app-build VDF:

```text
docker run --rm faulo/ci-tools:latest \
  steam-buildfile /workspace /workspace/logs 1000 2000=depot preview
```

## Repository layout

- `common/CiTools/` contains the cross-platform native Composer launcher.
- `common/CiTools.Tests/` contains daemon-free NUnit coverage.
- `linux/` contains the Linux Dockerfile and `ci-tools.packages` manifest.
- `windows/` contains the Windows Dockerfile, Chocolatey package metadata, PHP
  extension configuration, and native SteamCMD bootstrap launcher.
- `tests/` contains the cross-platform Pester image contract.

Both Dockerfiles use the repository root as their build context.

## Validation

Run unit tests with the .NET 9 SDK:

```text
dotnet test common/CiTools.Tests/CiTools.Tests.csproj --configuration Release
```

Build disposable candidates with explicit contexts:

```text
docker --context garl build --file linux/Dockerfile --tag tmp/ci-tools:latest .
docker --context dende build --file windows/Dockerfile --tag tmp/ci-tools:latest .
```

Run the complete image contract:

```text
pwsh ./.jenkins/Invoke-IntegrationTests.ps1 -Namespace tmp -Context garl
pwsh ./.jenkins/Invoke-IntegrationTests.ps1 -Namespace tmp -Context dende
```
