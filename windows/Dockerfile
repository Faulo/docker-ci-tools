ARG BASE_IMAGE=faulo/choco:windowsservercore-ltsc2019
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build

WORKDIR C:/solution
ENV NUGET_PACKAGES=C:/nuget/packages

COPY . .

# hadolint shell=powershell
RUN dotnet publish --runtime win-x64

FROM ${BASE_IMAGE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

WORKDIR C:/ci-tools

ENV COMPOSER_ALLOW_SUPERUSER=1
ENV CI_TOOLS_CALL_TIMEOUT=86400

# Runtime tools
COPY windows/ci-tools.nuspec C:/ci-tools/ci-tools.nuspec
# hadolint shell=powershell
RUN choco-install C:/ci-tools/ci-tools.nuspec; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install ci-tools dependencies' }; \
    $env:PATH = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    Remove-Item -LiteralPath C:/ci-tools/ci-tools.nuspec -Force

# A fresh process sees installer changes to the machine PATH.
# hadolint shell=powershell
RUN butler --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Butler smoke check failed' }; \
    node --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Node.js smoke check failed' }; \
    php --version; \
    if ($LASTEXITCODE -ne 0) { throw 'PHP smoke check failed' }; \
    composer --version; \
    if ($LASTEXITCODE -ne 0) { throw 'Composer smoke check failed' }; \
    pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion'; \
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell smoke check failed' }

COPY windows/php-extensions.ini C:/tools/php84/ci-tools-extensions.ini
# hadolint shell=powershell
RUN $imapVersion = (Invoke-WebRequest -UseBasicParsing 'https://pecl.php.net/rest/r/imap/latest.txt').Content.Trim(); \
    $imapArchive = 'C:\php-imap.zip'; \
    $imapRoot = 'C:\php-imap'; \
    $imapUri = 'https://windows.php.net/downloads/pecl/releases/imap/' + $imapVersion + '/php_imap-' + $imapVersion + '-8.4-nts-vs17-x64.zip'; \
    Invoke-WebRequest -Uri $imapUri -OutFile $imapArchive; \
    Expand-Archive -LiteralPath $imapArchive -DestinationPath $imapRoot; \
    Move-Item -LiteralPath (Join-Path $imapRoot 'php_imap.dll') -Destination C:\tools\php84\ext\php_imap.dll; \
    Remove-Item -LiteralPath $imapArchive, $imapRoot -Recurse -Force; \
    Get-Content C:/tools/php84/ci-tools-extensions.ini | Add-Content -Path C:/tools/php84/php.ini; \
    $modules = @(php -m); \
    if ($modules -notcontains 'imap') { throw 'PHP IMAP extension is unavailable' }; \
    if ($modules -notcontains 'xsl') { throw 'PHP XSL extension is unavailable' }

# Native launchers
COPY --from=build C:/solution/common/CiTools/bin/Release/net9.0/win-x64/publish/ci-tools-launcher.exe C:/ci-tools/bin/ci-tools-launcher.exe
COPY --from=build C:/solution/windows/SteamCmd/bin/Release/net9.0/win-x64/publish/steamcmd.exe C:/steamcmd/steamcmd.exe

# hadolint shell=powershell
RUN foreach ($command in @('steam-buildfile', 'steam-login')) { \
        & C:/ProgramData/chocolatey/tools/shimgen.exe -o ('C:/ci-tools/bin/' + $command + '.exe') -p C:/ci-tools/bin/ci-tools-launcher.exe -c $command; \
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath ('C:/ci-tools/bin/' + $command + '.exe'))) { throw ('Failed to create shim: ' + $command) } \
    }; \
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', ('C:\ci-tools\bin;' + $machinePath), 'Machine')

# SteamCMD is installed into its persistent volume on first use.
# hadolint shell=powershell
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

# Composer-backed CI tools
ARG TOOL_TIMEOUT=14400
# hadolint shell=powershell
RUN composer require ('slothsoft/unity') --no-interaction; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to require slothsoft/unity' }; \
    composer config process-timeout $env:TOOL_TIMEOUT; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure the Composer process timeout' }; \
    composer update --no-interaction --no-dev --optimize-autoloader --classmap-authoritative; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to update CI tool dependencies' }; \
    composer check-platform-reqs --no-dev; \
    if ($LASTEXITCODE -ne 0) { throw 'Composer platform requirements are not satisfied' }; \
    $buildfileHelp = steam-buildfile | Out-String; \
    if ($LASTEXITCODE -ne 0 -or $buildfileHelp -notmatch '(?m)^Create a steam build file\.') { throw 'steam-buildfile smoke check failed' }; \
    $loginHelp = steam-login unexpected-argument | Out-String; \
    if ($LASTEXITCODE -ne 0 -or $loginHelp -notmatch '(?m)^Sign in to Steam via steamcmd\.') { throw 'steam-login smoke check failed' }

VOLUME ["C:/steam"]
