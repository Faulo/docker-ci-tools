param(
    [Parameter(Mandatory)]
    [string] $Namespace,

    [Parameter(Mandatory)]
    [string] $Name,

    [Parameter(Mandatory)]
    [string] $Variant,

    [Parameter(Mandatory)]
    [string] $Context,

    [Parameter(Mandatory)]
    [string] $Image,

    [Parameter(Mandatory)]
    [string] $Os,

    [Parameter(Mandatory)]
    [AllowEmptyCollection()]
    [string[]] $DockerRunArguments
)

BeforeAll {
    . (Join-Path $PSScriptRoot '../.jenkins/Docker.ps1')

    function Invoke-ImageCommand {
        param(
            [Parameter(Mandatory)]
            [string[]] $Command,

            [string[]] $AdditionalRunArguments = @()
        )

        Get-DockerCommandResult `
            -Context $Context `
            -RunArguments ($DockerRunArguments + $AdditionalRunArguments) `
            -Arguments (@('run', '--rm', $Image) + $Command)
    }
}

Describe "CI tools image contract [$Os, $Image]" {
    BeforeAll {
        $imageConfig = Invoke-DockerOutput -Context $Context -Arguments @(
            'image', 'inspect', '--format', '{{json .Config}}', $Image
        ) | ConvertFrom-Json
        $entrypointProperty = $imageConfig.PSObject.Properties['Entrypoint']
        $commandProperty = $imageConfig.PSObject.Properties['Cmd']
        $environmentProperty = $imageConfig.PSObject.Properties['Env']
        $volumesProperty = $imageConfig.PSObject.Properties['Volumes']
        $portsProperty = $imageConfig.PSObject.Properties['ExposedPorts']
        $healthcheckProperty = $imageConfig.PSObject.Properties['Healthcheck']
        $entrypoint = @($(if ($null -eq $entrypointProperty -or $null -eq $entrypointProperty.Value) {
            @()
        } else {
            @($entrypointProperty.Value)
        }))
        $defaultCommand = @($(if ($null -eq $commandProperty -or $null -eq $commandProperty.Value) {
            @()
        } else {
            @($commandProperty.Value)
        }))
        $environment = @($(if ($null -eq $environmentProperty -or $null -eq $environmentProperty.Value) {
            @()
        } else {
            @($environmentProperty.Value)
        }))
        $volumes = @($(if ($null -eq $volumesProperty -or $null -eq $volumesProperty.Value) {
            @()
        } else {
            @($volumesProperty.Value.PSObject.Properties.Name)
        }))
        $ports = @($(if ($null -eq $portsProperty -or $null -eq $portsProperty.Value) {
            @()
        } else {
            @($portsProperty.Value.PSObject.Properties.Name)
        }))
        $healthcheck = if ($null -eq $healthcheckProperty) { $null } else { $healthcheckProperty.Value }
    }

    It 'is an x64 image for the selected daemon' {
        $platform = Invoke-DockerOutput -Context $Context -Arguments @(
            'image', 'inspect', '--format', '{{.Os}}/{{.Architecture}}', $Image
        )
        $platform | Should -BeExactly "$Os/amd64"
    }

    It 'uses Windows Server Core' -Skip:($Os -ne 'windows') {
        $result = Invoke-ImageCommand -Command @(
            'pwsh', '-NoLogo', '-NoProfile', '-Command',
            '(Get-ItemProperty ''HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'').InstallationType'
        )
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
        $result.Output | Should -Match 'Server Core'
    }

    It 'does not declare an entrypoint' {
        $entrypoint.Count | Should -Be 0
    }

    It 'inherits the platform base image command' {
        $expected = if ($Os -eq 'windows') {
            @('c:\windows\system32\cmd.exe')
        } else {
            @('bash')
        }
        $defaultCommand | Should -BeExactly $expected
    }

    It 'does not declare a health check, published port, or Unity environment' {
        $healthcheck | Should -BeNullOrEmpty
        $ports | Should -BeNullOrEmpty
        $environment | Should -Not -Match '(^|=)(COMPOSE_UNITY|UNITY_)'
    }

    It 'declares only the platform Steam state volume' {
        $expected = if ($Os -eq 'windows') { 'C:/steam' } else { '/root/Steam' }
        $volumes | Should -BeExactly @($expected)
    }

    It 'allows an arbitrary command to replace the default command' {
        $command = if ($Os -eq 'windows') {
            @('cmd.exe', '/S', '/C', 'exit 0')
        } else {
            @('/bin/sh', '-c', 'exit 0')
        }
        $result = Invoke-ImageCommand -Command $command
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
    }

    It 'keeps the Docker Pipeline keeper running' {
        $containerName = "ci-tools-keeper-$([guid]::NewGuid().ToString('N'))"
        $keeperCommand = if ($Os -eq 'windows') { 'cmd.exe' } else { 'cat' }

        try {
            Invoke-Docker -Context $Context -RunArguments $DockerRunArguments -Arguments @(
                'run', '--detach', '--tty', '--name', $containerName, $Image, $keeperCommand
            )
            $running = Invoke-DockerOutput -Context $Context -Arguments @(
                'container', 'inspect', '--format', '{{.State.Running}}', $containerName
            )
            $running | Should -BeExactly 'true'
        } finally {
            Get-DockerCommandResult -Context $Context -Arguments @(
                'container', 'rm', '--force', $containerName
            ) | Out-Null
        }
    }
}

Describe "Public command contract [$Os, $Image]" {
    It 'provides PHP 8.4' {
        $result = Invoke-ImageCommand -Command @(
            'php', '-r', 'echo PHP_MAJOR_VERSION, ".", PHP_MINOR_VERSION;'
        )
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
        ($result.Output | Out-String).Trim() | Should -BeExactly '8.4'
    }

    It 'provides all required public commands' {
        $commands = @('butler', 'composer', 'node', 'php', 'steamcmd', 'steam-buildfile', 'steam-login')
        $probe = if ($Os -eq 'windows') {
            $commandList = ($commands | ForEach-Object { "'$_'" }) -join ', '
            @(
                'pwsh', '-NoLogo', '-NoProfile', '-Command',
                ("`$missing = @($commandList) | Where-Object { -not (Get-Command `$_ -ErrorAction SilentlyContinue) }; " +
                 "if (`$missing) { Write-Error ('Missing commands: ' + (`$missing -join ', ')); exit 1 }")
            )
        } else {
            @(
                '/bin/sh', '-c',
                ("missing=''; for command in $($commands -join ' '); do " +
                 'command -v "$command" >/dev/null 2>&1 || missing="$missing $command"; done; ' +
                 'test -z "$missing" || { echo "Missing commands:$missing" >&2; exit 1; }')
            )
        }
        $result = Invoke-ImageCommand -Command $probe
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
    }

    It 'provides PowerShell Core only on Windows' {
        $probe = if ($Os -eq 'windows') {
            @('cmd.exe', '/S', '/C', 'where pwsh.exe >nul 2>&1')
        } else {
            @('/bin/sh', '-c', 'if command -v pwsh >/dev/null 2>&1; then exit 1; fi')
        }
        $result = Invoke-ImageCommand -Command $probe
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
    }

    It 'does not provide removed commands' {
        $commands = @(
            'compose-unity', 'unityhub', 'git', 'git-lfs', 'nano', 'blender',
            'python', 'python3', 'ffmpeg', 'dotnet', 'docfx'
        )
        $probe = if ($Os -eq 'windows') {
            $commandList = ($commands | ForEach-Object { "'$_'" }) -join ', '
            @(
                'pwsh', '-NoLogo', '-NoProfile', '-Command',
                ("`$present = @($commandList) | Where-Object { Get-Command `$_ -ErrorAction SilentlyContinue }; " +
                 "if (`$present) { Write-Error ('Unexpected commands: ' + (`$present -join ', ')); exit 1 }")
            )
        } else {
            @(
                '/bin/sh', '-c',
                ("present=''; for command in $($commands -join ' '); do " +
                 'command -v "$command" >/dev/null 2>&1 && present="$present $command"; done; ' +
                 'test -z "$present" || { echo "Unexpected commands:$present" >&2; exit 1; }')
            )
        }
        $result = Invoke-ImageCommand -Command $probe
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
    }

    It 'generates a Steam VDF while preserving argument boundaries' {
        $command = if ($Os -eq 'windows') {
            @(
                'pwsh', '-NoLogo', '-NoProfile', '-Command',
                ('$root = ''C:\Windows\Temp\ci tools contract\root''; ' +
                 '$logs = ''C:\Windows\Temp\ci tools contract\logs''; ' +
                 '$depot = Join-Path $root ''depot folder''; ' +
                 'New-Item -ItemType Directory -Force -Path $depot | Out-Null; ' +
                 'Set-Content -LiteralPath (Join-Path $depot ''payload.txt'') -Value ''payload''; ' +
                 '& steam-buildfile $root $logs 1000 ''2000=depot folder'' ''preview branch''')
            )
        } else {
            @(
                '/bin/sh', '-c',
                ('set -eu; root="/tmp/ci tools contract/root"; logs="/tmp/ci tools contract/logs"; ' +
                 'mkdir -p "$root/depot folder"; printf payload > "$root/depot folder/payload.txt"; ' +
                 'steam-buildfile "$root" "$logs" 1000 "2000=depot folder" "preview branch"')
            )
        }
        $result = Invoke-ImageCommand -Command $command
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
        $output = $result.Output | Out-String
        $output | Should -Match '"AppID"\s+"1000"'
        $output | Should -Match '"2000"'
        $output | Should -Match '"LocalPath"\s+"depot folder/\*"'
        $output | Should -Match '"SetLive"\s+"preview branch"'
    }

    It 'logs in to Steam with the configured runtime credentials' {
        $result = Invoke-ImageCommand -Command @('steam-login')
        $result.ExitCode | Should -Be 0 -Because ($result.Output | Out-String)
    }

    It 'bootstraps and reuses persistent SteamCMD state' {
        $volumeName = "ci-tools-steam-$([guid]::NewGuid().ToString('N'))"
        $volumePath = if ($Os -eq 'windows') { 'C:/steam' } else { '/root/Steam' }
        try {
            foreach ($attempt in 1..2) {
                $result = Invoke-ImageCommand `
                    -AdditionalRunArguments @('--volume', "${volumeName}:${volumePath}") `
                    -Command @('steamcmd', '+quit')
                $result.ExitCode | Should -Be 0 -Because "SteamCMD attempt $attempt failed: $($result.Output | Out-String)"
            }
        } finally {
            Get-DockerCommandResult -Context $Context -Arguments @('volume', 'rm', '--force', $volumeName) | Out-Null
        }
    }
}
