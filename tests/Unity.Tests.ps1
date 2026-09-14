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

BeforeDiscovery {
    $unityVersions = @(
        '2019.4.41f2'
        '2020.3.49f1'
        '2021.3.45f2'
        '2022.3.62f3'
        '6000.0.81f1'
    )
}

BeforeAll {
    . (Join-Path $PSScriptRoot '../.jenkins/Docker.ps1')
}

Describe "Unity behavior [$Os, $Image]" {
    Context 'with Unity <UnityVersion>' -ForEach @(
        $unityVersions | ForEach-Object { @{ UnityVersion = $_ } }
    ) {
        It 'runs the empty project tests' {
            Invoke-Docker -Context $Context -RunArguments $DockerRunArguments -Arguments @(
                'run', '--rm', $Image,
                'compose-unity', 'exec', 'unity-empty-project', 'test', $UnityVersion
            )
        }

        It 'runs the empty project tests with GPU acceleration' -Skip:($Os -ne 'windows') {
            $gpuArguments = @(
                '--isolation', 'process'
                '--device', 'class/5B45201D-F2F2-4F3B-85BB-30FF1F953599'
                '--env', 'UNITY_NO_GRAPHICS=0'
            )

            Invoke-Docker -Context $Context -RunArguments ($DockerRunArguments + $gpuArguments) -Arguments @(
                'run', '--rm', $Image,
                'compose-unity', 'exec', 'unity-empty-project', 'test', $UnityVersion
            )
        }
    }
}
