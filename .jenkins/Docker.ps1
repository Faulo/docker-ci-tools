function Get-DockerCommandArguments {
    param(
        [Parameter(Mandatory)]
        [string] $Context,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string[]] $RunArguments = @()
    )

    $commandArguments = @('--context', $Context)
    if ($Arguments.Count -gt 0 -and $Arguments[0] -eq 'run') {
        $commandArguments += 'run'
        $commandArguments += $RunArguments
        if ($Arguments.Count -gt 1) {
            $commandArguments += $Arguments[1..($Arguments.Count - 1)]
        }
    } else {
        $commandArguments += $Arguments
    }

    return $commandArguments
}

function Get-DockerCommandResult {
    param(
        [Parameter(Mandatory)]
        [string] $Context,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string[]] $RunArguments = @()
    )

    $commandArguments = Get-DockerCommandArguments -Context $Context -Arguments $Arguments -RunArguments $RunArguments
    $output = @(& docker @commandArguments 2>&1)

    [pscustomobject] @{
        Arguments = $commandArguments
        ExitCode = $LASTEXITCODE
        Output = $output
    }
}

function Invoke-Docker {
    param(
        [Parameter(Mandatory)]
        [string] $Context,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string[]] $RunArguments = @()
    )

    $result = Get-DockerCommandResult -Context $Context -Arguments $Arguments -RunArguments $RunArguments
    if ($result.ExitCode -ne 0) {
        throw "Docker command failed with exit code $($result.ExitCode): docker $($result.Arguments -join ' ')`n$($result.Output | Out-String)"
    }
}

function Invoke-DockerOutput {
    param(
        [Parameter(Mandatory)]
        [string] $Context,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string[]] $RunArguments = @()
    )

    $result = Get-DockerCommandResult -Context $Context -Arguments $Arguments -RunArguments $RunArguments
    if ($result.ExitCode -ne 0) {
        throw "Docker command failed with exit code $($result.ExitCode): docker $($result.Arguments -join ' ')`n$($result.Output | Out-String)"
    }

    return ($result.Output | Out-String).Trim()
}
