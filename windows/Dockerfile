ARG OS_BASE=ltsc2019
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build

WORKDIR C:/solution

ENV NUGET_PACKAGES=C:/nuget/packages

COPY . .

RUN dotnet publish \
        --configuration Release \
        --runtime win-x64 \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:UseAppHost=true \
        --property:DebugType=None

FROM mcr.microsoft.com/windows:${OS_BASE}

ARG OS_BASE

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

WORKDIR C:/compose-unity

# The selectable full Windows base supplies the desktop WinRT classes required
# by Unity. Shell compatibility artifacts below are validated per OS family.
ENV COMPLUS_RUNNING_IN_CONTAINER=1
ENV COMPLUS_NGenProtectedProcess_FeatureEnabled=0
RUN $policyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'; \
    Set-ItemProperty -LiteralPath $policyKey -Name ConsentPromptBehaviorAdmin -Value 0; \
    Set-ItemProperty -LiteralPath $policyKey -Name PromptOnSecureDesktop -Value 0

# Chocolatey
# Install a pinned Chocolatey package directly so its bootstrap remains
# reproducible and does not depend on the current remote installation script.
RUN $archive = 'C:\chocolatey.1.4.0.zip'; \
    $extractRoot = 'C:\chocolatey.1.4.0'; \
    Invoke-WebRequest -Uri 'https://community.chocolatey.org/api/v2/package/chocolatey/1.4.0' -OutFile $archive; \
    $actualHash = (Get-FileHash -Algorithm SHA256 $archive).Hash; \
    if ($actualHash -ne '1660261EDD358FF5EA84A503CA3F01A24DDB0E281002BD4237B1071289161A26') { \
        throw ('Chocolatey package checksum mismatch: ' + $actualHash) \
    }; \
    Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot; \
    $env:ChocolateyInstall = 'C:\ProgramData\chocolatey'; \
    $env:ChocolateyPackageVersion = '1.4.0'; \
    [Environment]::SetEnvironmentVariable('ChocolateyInstall', $env:ChocolateyInstall, 'Machine'); \
    & (Join-Path $extractRoot 'tools\chocolateyInstall.ps1'); \
    Remove-Item -LiteralPath $archive, $extractRoot -Recurse -Force; \
    choco --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install Chocolatey' }

# Keep the full desktop API surface visible as an explicit image invariant.
RUN foreach ($dll in 'ddraw.dll', 'dsound.dll', 'glu32.dll', 'opengl32.dll', 'avicap32.dll', 'msvfw32.dll') { \
        $path = Join-Path $env:WINDIR ('System32\' + $dll); \
        if (-not (Test-Path -LiteralPath $path)) { throw ('Desktop runtime DLL not found: ' + $path) } \
    }; \
    foreach ($typeName in 'Windows.UI.ViewManagement.UISettings', 'Windows.UI.ViewManagement.AccessibilitySettings') { \
        $type = [Type]::GetType(($typeName + ', Windows, ContentType=WindowsRuntime'), $true); \
        $instance = [Activator]::CreateInstance($type); \
        if ($null -eq $instance) { throw ('Failed to activate WinRT type: ' + $typeName) } \
    }

# Tools
ARG DOTNET_VERSION=9.0
ENV DOTNET_VERSION="${DOTNET_VERSION}"
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
ENV DOTNET_CLI_UI_LANGUAGE=en
ARG NODE_VERSION=24
ENV NODE_VERSION="${NODE_VERSION}"
ARG BLENDER_SERIES=4.5
COPY windows/compose-unity.nuspec compose-unity.nuspec
RUN if ($env:DOTNET_VERSION -notmatch '^\d+\.\d+$') { throw "Invalid .NET SDK feature version: $env:DOTNET_VERSION" }; \
    if ($env:NODE_VERSION -notmatch '^\d+$') { throw "Invalid Node.js major version: $env:NODE_VERSION" }; \
    $seriesMatch = [regex]::Match($env:BLENDER_SERIES, '^(?<major>\d+)\.(?<minor>\d+)$'); \
    if (-not $seriesMatch.Success) { throw "Invalid Blender series: $env:BLENDER_SERIES" }; \
    $nextSeries = '{0}.{1}' -f $seriesMatch.Groups['major'].Value, ([int]$seriesMatch.Groups['minor'].Value + 1); \
    [xml]$packageSpec = Get-Content -LiteralPath compose-unity.nuspec; \
    $dotnetDependencies = @($packageSpec.package.metadata.dependencies.dependency | Where-Object id -like 'dotnet-*-sdk'); \
    if ($dotnetDependencies.Count -ne 1) { throw "Expected one .NET SDK dependency, found $($dotnetDependencies.Count)" }; \
    $dotnetDependencies[0].SetAttribute('id', 'dotnet-' + $env:DOTNET_VERSION + '-sdk'); \
    $nodeDependencies = @($packageSpec.package.metadata.dependencies.dependency | Where-Object id -eq 'nodejs'); \
    if ($nodeDependencies.Count -ne 1) { throw "Expected one Node.js dependency, found $($nodeDependencies.Count)" }; \
    $nodeDependencies[0].SetAttribute('version', '[' + $env:NODE_VERSION + ',' + ([int]$env:NODE_VERSION + 1) + ')'); \
    $blenderDependencies = @($packageSpec.package.metadata.dependencies.dependency | Where-Object id -eq 'blender'); \
    if ($blenderDependencies.Count -ne 1) { throw "Expected one Blender dependency, found $($blenderDependencies.Count)" }; \
    $blenderDependencies[0].SetAttribute('version', '[' + $env:BLENDER_SERIES + ',' + $nextSeries + ')'); \
    $packageSpec.Save((Resolve-Path -LiteralPath compose-unity.nuspec)); \
    choco pack compose-unity.nuspec; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to pack compose-unity' }; \
    $dependencyInstallSucceeded = $false; \
    for ($attempt = 1; $attempt -le 5; $attempt++) { \
        choco install compose-unity --no-progress --yes --ignore-checksums --execution-timeout=7200 --source '.;https://community.chocolatey.org/api/v2/'; \
        if ($LASTEXITCODE -eq 0) { \
            $dependencyInstallSucceeded = $true; \
            break \
        }; \
        Write-Warning ('Chocolatey dependency installation attempt ' + $attempt + ' failed'); \
        if ($attempt -lt 5) { Start-Sleep -Seconds (30 * $attempt) } \
    }; \
    if (-not $dependencyInstallSucceeded) { throw 'Failed to install compose-unity dependencies after 5 attempts' }; \
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    $env:PATH = $machinePath; \
    $installedSdkVersion = dotnet --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to query the installed .NET SDK' }; \
    if ($installedSdkVersion -notmatch ('^' + [regex]::Escape($env:DOTNET_VERSION) + '\.')) { throw "Unexpected .NET SDK version: $installedSdkVersion" }; \
    $dotnetToolsDirectory = Join-Path $env:USERPROFILE '.dotnet\tools'; \
    [Environment]::SetEnvironmentVariable('Path', $dotnetToolsDirectory + ';' + $machinePath, 'Machine'); \
    Remove-Item -Force *.nuspec; \
    Remove-Item -Force *.nupkg

# DocFX
RUN dotnet tool update --global docfx; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install DocFX' }; \
    docfx --version; \
    if ($LASTEXITCODE -ne 0) { throw 'DocFX smoke check failed' }

# Unity-generated projects can target .NET Framework 4.7.1 even when a newer
# runtime is installed. The Developer Pack installer hangs under Hyper-V
# isolation, so install Microsoft's reference-assemblies package for MSBuild.
RUN $package = 'C:\net471-reference-assemblies.nupkg'; \
    $extractRoot = 'C:\net471-reference-assemblies'; \
    $packageUri = 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net471/1.0.3/microsoft.netframework.referenceassemblies.net471.1.0.3.nupkg'; \
    Invoke-WebRequest -Uri $packageUri -OutFile $package; \
    $actualHash = (Get-FileHash -Algorithm SHA256 $package).Hash; \
    if ($actualHash -ne '2D1662724B3794992640C59BFCEB2F010E62A3BDB581A626D1B6D7BD5149D575') { \
        throw ".NET Framework 4.7.1 reference assemblies checksum mismatch: $actualHash" \
    }; \
    $sevenZip = 'C:\ProgramData\chocolatey\lib\7zip.portable\tools\7z.exe'; \
    $extractArgument = '-o' + $extractRoot; \
    & $sevenZip x $package $extractArgument -y | Out-Null; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract .NET Framework 4.7.1 reference assemblies' }; \
    $packageAssemblies = Join-Path $extractRoot 'build\.NETFramework\v4.7.1'; \
    $referenceDirectory = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1'; \
    New-Item -ItemType Directory -Path $referenceDirectory -Force | Out-Null; \
    Copy-Item -Path (Join-Path $packageAssemblies '*') -Destination $referenceDirectory -Recurse -Force; \
    $referenceAssembly = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\mscorlib.dll'; \
    if (-not (Test-Path -LiteralPath $referenceAssembly)) { \
        throw '.NET Framework 4.7.1 reference assemblies were not installed' \
    }; \
    Remove-Item -LiteralPath $package, $extractRoot -Recurse -Force

# Use the supported Visual Studio 2019 servicing baseline because Visual Studio
# 2022 MSBuild is incompatible with the .NET Framework build host used here.
COPY windows/WindowsBase C:/compose-unity/WindowsBase
RUN & 'C:\compose-unity\WindowsBase\install.ps1'; \
    Remove-Item -LiteralPath C:\compose-unity\WindowsBase -Recurse -Force

# Unity publishes the 3.12.1 installer under its 3.12.0 archive path. Its NSIS
# process can hang in a container, so fall back to its verified app archive.
ENV UNITY_HUB_VERSION=3.12.1
RUN $installer = 'C:\UnityHubSetup.exe'; \
    $hubDirectory = 'C:\Program Files\Unity Hub'; \
    $hubExecutable = Join-Path $hubDirectory 'Unity Hub.exe'; \
    Invoke-WebRequest -Uri 'https://public-cdn.cloud.unity3d.com/hub/3.12.0/UnityHubSetup.exe' -OutFile $installer; \
    $actualHash = (Get-FileHash -Algorithm SHA256 $installer).Hash; \
    if ($actualHash -ne '0B8E6941A6A2A7C1DF68B16451E4CC7F8F633C6B5488B21A47860179FD5D8802') { \
        throw "Unity Hub installer checksum mismatch: $actualHash" \
    }; \
    $extractRoot = 'C:\UnityHubSetup'; \
    $sevenZip = 'C:\ProgramData\chocolatey\lib\7zip.portable\tools\7z.exe'; \
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue; \
    $extractArgument = '-o' + $extractRoot; \
    & $sevenZip x $installer $extractArgument -y | Out-Null; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract the Unity Hub installer' }; \
    $appArchives = @(Get-ChildItem -LiteralPath $extractRoot -Filter 'app-64.7z' -Recurse); \
    if ($appArchives.Count -ne 1) { throw "Expected one Unity Hub app archive, found $($appArchives.Count)" }; \
    $appArchive = $appArchives[0].FullName; \
    $uninstaller = (Get-ChildItem -LiteralPath $extractRoot -Filter 'Uninstall Unity Hub.exe' -Recurse | Select-Object -First 1).FullName; \
    $process = Start-Process -FilePath $installer -ArgumentList '/S' -PassThru; \
    $installerExited = $process.WaitForExit(30000); \
    $needsExtraction = -not $installerExited; \
    if ($installerExited -and $process.ExitCode -ne 0) { $needsExtraction = $true }; \
    if (-not (Test-Path -LiteralPath $hubExecutable)) { $needsExtraction = $true }; \
    if ($needsExtraction) { \
        if (-not $process.HasExited) { \
            taskkill.exe /PID $process.Id /T /F | Out-Null; \
            if (-not $process.HasExited) { $process.Kill() }; \
            $process.WaitForExit() \
        }; \
        Remove-Item -LiteralPath $hubDirectory -Recurse -Force -ErrorAction SilentlyContinue; \
        $hubExtractArgument = '-o' + $hubDirectory; \
        & $sevenZip x $appArchive $hubExtractArgument -y | Out-Null; \
        if ($LASTEXITCODE -ne 0) { throw 'Failed to extract the Unity Hub application' }; \
        Copy-Item -LiteralPath $uninstaller -Destination $hubDirectory \
    }; \
    $installedVersion = (Get-Item $hubExecutable).VersionInfo.FileVersion; \
    if ($installedVersion -ne $env:UNITY_HUB_VERSION) { throw "Unexpected Unity Hub version: $installedVersion" }; \
    $hubRegistryKey = 'HKLM:\SOFTWARE\Unity Technologies\Hub'; \
    New-Item -Path $hubRegistryKey -Force | Out-Null; \
    New-ItemProperty -Path $hubRegistryKey -Name 'InstallLocation' -Value $hubDirectory -PropertyType String -Force | Out-Null; \
    Remove-Item -LiteralPath $extractRoot -Recurse -Force; \
    Remove-Item -Force $installer

# Resume large Editor downloads after temporary CDN stalls and launch the
# installer helper directly because Windows containers cannot display UAC.
COPY windows/patch-unity-hub.js C:/compose-unity/patch-unity-hub.js
RUN node C:\compose-unity\patch-unity-hub.js; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to patch Unity Hub for Windows containers' }; \
    Remove-Item -Force C:\compose-unity\patch-unity-hub.js

COPY windows/php-extensions.ini C:/tools/php82/compose-unity-extensions.ini
RUN Get-Content C:/tools/php82/compose-unity-extensions.ini | Add-Content -Path C:/tools/php82/php.ini

RUN $blenderDirectory = 'C:\Program Files\Blender Foundation\Blender ' + $env:BLENDER_SERIES; \
    $blenderExecutable = Join-Path $blenderDirectory 'blender.exe'; \
    if (-not (Test-Path -LiteralPath $blenderExecutable)) { throw "Blender executable not found: $blenderExecutable" }; \
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', $blenderDirectory + ';' + $machinePath, 'Machine'); \
    $blenderFileClass = 'blender.' + $env:BLENDER_SERIES; \
    $classesRoot = 'HKLM:\SOFTWARE\Classes'; \
    foreach ($extension in '.blend', '.fbx') { \
        $extensionKey = Join-Path $classesRoot $extension; \
        New-Item -Path $extensionKey -Force | Out-Null; \
        Set-Item -Path $extensionKey -Value $blenderFileClass \
    }; \
    $openCommandKey = Join-Path $classesRoot "$blenderFileClass\shell\open\command"; \
    New-Item -Path $openCommandKey -Force | Out-Null; \
    $quote = [char]34; \
    $openCommand = $quote + $blenderExecutable + $quote + ' ' + $quote + '%1' + $quote; \
    Set-Item -Path $openCommandKey -Value $openCommand; \
    foreach ($extension in '.blend', '.fbx') { \
        $registeredClass = (Get-Item (Join-Path $classesRoot $extension)).GetValue(''); \
        if ($registeredClass -ne $blenderFileClass) { throw "Unexpected $extension file association: $registeredClass" } \
    }; \
    $registeredCommand = (Get-Item $openCommandKey).GetValue(''); \
    if ($registeredCommand -ne $openCommand) { throw "Unexpected Blender open command: $registeredCommand" }

# Windows containers expose SHELL32 but its desktop-shell implementation cannot
# reliably resolve associations on mounted paths or launch executables without
# Explorer. Preserve every original export through a proxy and replace only
# FindExecutableW and ShellExecuteExW with container-safe implementations.
COPY windows/Shell32Proxy C:/compose-unity/Shell32Proxy
RUN & 'C:\compose-unity\Shell32Proxy\install.ps1'

# Run both architectures in a fresh layer so KnownDLL resolution loads the
# proxies rather than the original DLLs mapped by the replacement layer.
RUN & 'C:\compose-unity\Shell32Proxy\shell32-proxy-test-x64.exe'; \
    if ($LASTEXITCODE -ne 0) { throw ('64-bit SHELL32 proxy test failed with exit code {0}' -f $LASTEXITCODE) }; \
    & 'C:\compose-unity\Shell32Proxy\shell32-proxy-test-x86.exe'; \
    if ($LASTEXITCODE -ne 0) { throw ('32-bit SHELL32 proxy test failed with exit code {0}' -f $LASTEXITCODE) }; \
    Remove-Item -LiteralPath C:\compose-unity\Shell32Proxy -Recurse -Force

# Test
RUN git config --global --add safe.directory *; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure Git safe directories' }; \
    curl.exe --version; \
    if ($LASTEXITCODE -ne 0) { throw 'curl smoke check failed' }; \
    git --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Git smoke check failed' }; \
    php --version; \
    if ($LASTEXITCODE -ne 0) { throw 'PHP smoke check failed' }; \
    composer --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Composer smoke check failed' }; \
    blender --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Blender smoke check failed' }; \
    butler --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Butler smoke check failed' }; \
    npm --version; \
    if ($LASTEXITCODE -ne 0) { throw 'npm smoke check failed' }; \
    $installedNodeVersion = node --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Node.js smoke check failed' }; \
    if ($installedNodeVersion -notmatch ('^v' + [regex]::Escape($env:NODE_VERSION) + '\.')) { throw "Unexpected Node.js version: $installedNodeVersion" }; \
    git lfs version; \
    if ($LASTEXITCODE -ne 0) { throw 'Git LFS smoke check failed' }; \
    python --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Python smoke check failed' }; \
    ffmpeg -version; \
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg smoke check failed' }; \
    dotnet --version; \
    if ($LASTEXITCODE -ne 0) { throw '.NET smoke check failed' }

# Farah
ENV COMPOSE_UNITY="compose-unity"
ENV COMPOSER_ALLOW_SUPERUSER="1"

ENV UNITY_LOGGING="stdin stdout stderr"
ENV UNITY_ACCELERATOR_ENDPOINT=""
ENV UNITY_NO_GRAPHICS="1"

# Windows containers cannot start Electron's sandboxed GPU process. Keep Unity's
# documented CLI syntax externally and translate it before launching Hub.
COPY --from=build C:/solution/windows/UnityHub/bin/Release/net9.0/win-x64/publish/unity-hub-launcher.exe C:/compose-unity/publish/unity-hub-launcher.exe
COPY --from=build C:/solution/windows/SteamCmd/bin/Release/net9.0/win-x64/publish/steamcmd.exe C:/compose-unity/publish/steamcmd.exe
COPY --from=build C:/solution/common/ComposeUnity/bin/Release/net9.0/win-x64/publish/compose-unity.exe C:/compose-unity/publish/compose-unity.exe
RUN $hubArtifact = 'C:\compose-unity\publish\unity-hub-launcher.exe'; \
    $steamCmdArtifact = 'C:\compose-unity\publish\steamcmd.exe'; \
    $composeArtifact = 'C:\compose-unity\publish\compose-unity.exe'; \
    $hub = 'C:\Program Files\Unity Hub\Unity Hub.exe'; \
    Move-Item -LiteralPath $hub -Destination 'C:\Program Files\Unity Hub\Unity Hub.real.exe'; \
    Move-Item -LiteralPath $hubArtifact -Destination $hub; \
    New-Item -ItemType Directory -Path C:\steamcmd | Out-Null; \
    Move-Item -LiteralPath $steamCmdArtifact -Destination C:\steamcmd\steamcmd.exe; \
    Copy-Item -LiteralPath $composeArtifact -Destination C:\Windows\compose-unity.exe; \
    New-Item -ItemType HardLink -Path C:\Windows\compose-unity-sidecar.exe -Target C:\Windows\compose-unity.exe | Out-Null; \
    Remove-Item -LiteralPath C:\compose-unity\publish -Recurse -Force

RUN $archive = 'C:\steamcmd.zip'; \
    Invoke-WebRequest -Uri 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $archive; \
    $extractRoot = 'C:\steamcmd-installer'; \
    Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot; \
    $installer = Join-Path $extractRoot 'steamcmd.exe'; \
    $installerFiles = @(Get-ChildItem -LiteralPath $extractRoot -File -Recurse); \
    if ($installerFiles.Count -ne 1 -or $installerFiles[0].FullName -ne $installer) { throw 'Unexpected SteamCMD archive contents' }; \
    Move-Item -LiteralPath $installer -Destination C:\steamcmd\steamcmd-installer.exe; \
    Remove-Item -LiteralPath $archive, $extractRoot -Recurse -Force; \
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', 'C:\steamcmd;' + $machinePath, 'Machine'); \
    if (Test-Path -LiteralPath C:\steam) { throw 'SteamCMD volume directory must not be populated in the image' }

ENV COMPOSE_UNITY_CALL_TIMEOUT=86400
ENV COMPOSE_UNITY_MCP=0

ARG UNITY_TIMEOUT="14400"
ARG SLOTHSOFT_UNITY_VERSION=2.22
RUN compose-unity require ('slothsoft/unity:^' + $env:SLOTHSOFT_UNITY_VERSION); \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to require slothsoft/unity' }; \
    compose-unity config process-timeout $env:UNITY_TIMEOUT; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure the Composer process timeout' }; \
    compose-unity update --no-interaction --no-dev --optimize-autoloader --classmap-authoritative; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to update compose-unity dependencies' }

# Test
RUN compose-unity exec unity-build; \
    if ($LASTEXITCODE -ne 0) { throw 'compose-unity unity-build smoke check failed' }; \
    compose-unity exec unity-help; \
    if ($LASTEXITCODE -ne 0) { throw 'compose-unity unity-help smoke check failed' }; \
    Remove-Item -LiteralPath C:\ProgramData\compose-unity -Recurse -Force

# PowerShell
ARG POWERSHELL_MAJOR=7

ADD https://api.github.com/repos/PowerShell/PowerShell/releases?per_page=100 C:/powershell-releases.json

RUN $releases = Get-Content C:/powershell-releases.json -Raw | ConvertFrom-Json; \
    $release = $releases | \
      Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match ('^v' + $env:POWERSHELL_MAJOR + '\.\d+\.\d+$') } | \
      Sort-Object { [version]$_.tag_name.Substring(1) } | \
      Select-Object -Last 1; \
    if (-not $release) { throw ('No stable PowerShell ' + $env:POWERSHELL_MAJOR + ' release found') }; \
    $powershellVersion = $release.tag_name.Substring(1); \
    $archiveName = 'PowerShell-' + $powershellVersion + '-win-x64.zip'; \
    $archive = 'C:/' + $archiveName; \
    $asset = $release.assets | Where-Object name -eq $archiveName; \
    $hashesAsset = $release.assets | Where-Object name -eq 'hashes.sha256'; \
    if (-not $asset -or -not $hashesAsset) { throw ('PowerShell ' + $powershellVersion + ' release assets are incomplete') }; \
    curl.exe -fsSL $asset.browser_download_url -o $archive; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to download PowerShell' }; \
    curl.exe -fsSL $hashesAsset.browser_download_url -o C:/powershell-hashes; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to download PowerShell hashes' }; \
    $hashLine = Get-Content C:/powershell-hashes | Where-Object { $_ -match ('\*' + [regex]::Escape($archiveName) + '$') }; \
    if (-not $hashLine) { throw ('No checksum found for ' + $archiveName) }; \
    $expectedHash = ($hashLine -split '\s+')[0]; \
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash; \
    if ($actualHash -ne $expectedHash) { throw ('Checksum verification failed for ' + $archiveName) }; \
    $destination = 'C:/Program Files/PowerShell/7'; \
    New-Item -ItemType Directory -Force $destination | Out-Null; \
    Expand-Archive -LiteralPath $archive -DestinationPath $destination; \
    Remove-Item -LiteralPath $archive,C:/powershell-hashes,C:/powershell-releases.json; \
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', ($destination + ';' + $machinePath), 'Machine'); \
    $env:Path = $destination + ';' + $env:Path; \
    pwsh -NoLogo -NoProfile -Command \
        'if ($PSVersionTable.PSVersion.Major -ne [int]$env:POWERSHELL_MAJOR) { exit 1 }'; \
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell smoke check failed' }

VOLUME ["C:/Program Files/Unity/Hub/Editor", "C:/steam", "C:/Users/ContainerAdministrator/AppData/Roaming/Unity", "C:/Users/ContainerAdministrator/AppData/Roaming/UnityHub", "C:/Users/ContainerAdministrator/AppData/Local/Unity", "C:/ProgramData/Unity"]

EXPOSE 8080

HEALTHCHECK --interval=60s --timeout=10s --start-period=30s --retries=3 \
    CMD ["compose-unity", "sidecar", "health"]

CMD ["compose-unity", "sidecar"]
