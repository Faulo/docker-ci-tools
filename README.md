# CI tools for Linux and Windows containers

[![Docker](https://github.com/Faulo/docker-ci-tools/actions/workflows/docker.yml/badge.svg?branch=main)](https://github.com/Faulo/docker-ci-tools/actions/workflows/docker.yml) [![Docker image updated](https://img.shields.io/docker/last-updated/faulo/ci-tools/latest)](https://hub.docker.com/r/faulo/ci-tools/tags?name=latest) [![License: MIT](https://img.shields.io/github/license/Faulo/docker-ci-tools)](LICENSE) [![Platforms: Linux and Windows amd64](https://img.shields.io/badge/platforms-Linux%20%7C%20Windows%20amd64-0078D4)](#available-images)

[`faulo/ci-tools`](https://hub.docker.com/r/faulo/ci-tools) provides command-line
tools for uploading builds to Steam and itch.io. Run individual commands with
Docker, use the image in a CI pipeline, or extend it with your own build tools.

## Available images

| Tag | Platforms | Runtime bases |
| --- | --- | --- |
| `latest` | Linux amd64, Windows amd64 | Debian Trixie Slim; Windows Server Core LTSC 2019 through [`faulo/choco`](https://github.com/Faulo/docker-choco) |
| `latest-linux-amd64` | Linux amd64 | Debian Trixie Slim |
| `latest-windows-ltsc2019` | Windows amd64 | Windows Server Core LTSC 2019 through `faulo/choco` |

Docker selects the platform matching your daemon. Windows containers require a
compatible Windows host. Pin an individual image digest when you need an exact
image. Images are rebuilt monthly to pick up dependency updates.

Use `latest` for automatic platform selection, or a target-specific tag to
select Linux amd64 or Windows LTSC 2019 explicitly. The intermediate
`latest-linux` and `latest-windows` tags select an operating system.
Tags receive updates in place; pin an image digest for a reproducible base.

## Included tools

| Command | Purpose |
| --- | --- |
| `butler` | Upload builds to itch.io. |
| `steamcmd` | Download and publish Steam content. |
| `steam-buildfile [ARG...]` | Generate a Steam app-build VDF. |
| `steam-login [ARG...]` | Sign in to Steam using Steam and mailbox credentials. |
| `php`, `composer`, `node` | Runtime tools, including PHP 8.4 with IMAP and XSL. |
| `pwsh` | PowerShell Core, available on Windows. |

The Steam helper commands use [`slothsoft/unity`](https://packagist.org/packages/slothsoft/unity)
through Composer. Run `steam-buildfile` without arguments, or
`steam-login unexpected-argument`, to display their usage.

## Quick start

Generate a Steam app-build VDF on Linux, with your content in a local `depot`
directory:

```sh
docker run --rm -v "$PWD:/workspace" faulo/ci-tools:latest steam-buildfile /workspace /workspace/logs 1000 2000=depot preview
```

Upload a local build to itch.io on Linux:

```sh
docker run --rm -e BUTLER_API_KEY -v "$PWD/build:/build:ro" faulo/ci-tools:latest butler push /build username/game:channel
```

The image declares no entrypoint, so you can replace the complete command.
The default command is `bash` on Linux and `cmd.exe` on Windows. Interactive
containers and overridden commands such as `cat` or `cmd.exe` are supported.

Commands are on PATH and also work with `docker exec`:

```sh
docker run -dit --name ci-tools faulo/ci-tools:latest
docker exec ci-tools steam-buildfile
docker rm -f ci-tools
```

The explicit forms `ci-tools-launcher steam-buildfile [ARG...]` and
`ci-tools-launcher steam-login [ARG...]` are also available. Shorthands use
executable shell wrappers on Linux and ShimGen executables on Windows. The
launcher preserves the working directory, argument boundaries, attached
standard streams, and child exit status, and manages termination and deadlines.

## Persistent Steam state

Mount a named volume to retain SteamCMD downloads and authentication state:

| Platform | Mount |
| --- | --- |
| Linux | `steam:/root/Steam` |
| Windows | `steam:C:/steam` |

For example, on Linux:

```sh
docker run --rm -v steam:/root/Steam faulo/ci-tools:latest steamcmd +quit
```

On Windows:

```powershell
docker run --rm -v steam:C:/steam faulo/ci-tools:latest steamcmd +quit
```

The Windows launcher bootstraps SteamCMD into the volume on first use.
On Linux, `steamcmd` invokes Valve's launch script with the native 64-bit client,
so self-updates can restart the client before running the requested commands.

## Credentials and timeouts

Steam login accepts environment variables or file-backed credentials:

| Credential | Environment variables | File-backed variables |
| --- | --- | --- |
| Steam | `STEAM_CREDENTIALS_USR`, `STEAM_CREDENTIALS_PSW` | `STEAM_CREDENTIALS_USR_FILE`, `STEAM_CREDENTIALS_PSW_FILE` |
| Mailbox | `EMAIL_CREDENTIALS_USR`, `EMAIL_CREDENTIALS_PSW` | `EMAIL_CREDENTIALS_USR_FILE`, `EMAIL_CREDENTIALS_PSW_FILE` |

Each username/password pair may mix direct and file-backed inputs. A variable
and its `_FILE` form cannot both be set, and configured pairs must be complete.
Missing, unreadable, or empty credential files fail before the child command
starts. One trailing line ending is removed; other whitespace is preserved.
Resolved values are passed only to the child process.

With credentials already exported in your shell, sign in on Linux:

```sh
docker run --rm -e STEAM_CREDENTIALS_USR -e STEAM_CREDENTIALS_PSW -e EMAIL_CREDENTIALS_USR -e EMAIL_CREDENTIALS_PSW -v steam:/root/Steam faulo/ci-tools:latest steam-login
```

Set `CI_TOOLS_CALL_TIMEOUT` to limit each Steam helper call in seconds. Its
default is `86400`; `0` disables the launcher deadline. A deadline returns exit
code `124`, and an interrupted call returns `130`.

## Extending and building the image

Both Dockerfiles use the repository root as their build context. Shared launcher
sources live in `common/`; platform-specific packages and inputs live in
`linux/` and `windows/`. A .NET SDK compiles the native launchers in a separate
build stage.

| Build argument | Default | Purpose |
| --- | --- | --- |
| `TOOL_TIMEOUT` | `14400` | Composer's child-process timeout in seconds. |
| `NODE_VERSION` | `24` | Linux Node.js major release. |
| `BASE_IMAGE` | `faulo/choco:windowsservercore-ltsc2019` | Windows runtime base with Chocolatey and `choco-install`. |

Build from the repository root using a Docker engine configured for the chosen
container OS. Run the appropriate command for that engine:

```sh
docker build -f linux/Dockerfile -t tmp/ci-tools:latest .
docker build -f windows/Dockerfile -t tmp/ci-tools:latest .
```

## Development checks

Run the launcher unit tests from the repository root with the .NET 9 SDK:

```sh
dotnet test common/CiTools.Tests/CiTools.Tests.csproj --configuration Release
```

The repository also includes image integration tests for the installed tools,
launcher behavior, SteamCMD state, and authenticated Steam login on Linux and
Windows. Steam login coverage requires the credentials listed above.

## Maintenance and updates

Linux amd64 and Windows Server Core LTSC 2019 images are rebuilt monthly.
Recreate containers or rebuild downstream images with `docker build --pull`
to adopt updates. Persist Steam state separately from the container layer.

## Support and contributions

[Open an issue](https://github.com/Faulo/docker-ci-tools/issues) with the image
tag or digest, container OS, command, and a minimal reproduction. For installer
failures, include the package version and Windows host version and isolation
mode. Remove credentials from logs before sharing them.

Pull requests are welcome. Include tests for changes to launcher behavior and
update the documentation when public commands or image contents change.

## License

The original code, Dockerfiles, scripts, and documentation are licensed under
the [MIT License](LICENSE). Bundled tools and base images retain their respective
licenses; Steam and itch.io services remain subject to their own terms.
