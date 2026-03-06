[CmdletBinding()]
param(
    [string]$SolutionPath,
    [string]$MsBuildPath,
    [string]$RoslynCscPath,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path $scriptDir -Parent

if ([string]::IsNullOrWhiteSpace($SolutionPath)) {
    $SolutionPath = Join-Path $repoRoot 'de4dot.sln'
}

if (-not (Test-Path -LiteralPath $SolutionPath)) {
    throw "Solution not found: $SolutionPath"
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $candidates = @(
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe',
        'C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    )
    $MsBuildPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path -LiteralPath $MsBuildPath)) {
    throw 'MSBuild.exe not found. Pass -MsBuildPath explicitly.'
}

if ([string]::IsNullOrWhiteSpace($RoslynCscPath)) {
    $roslynCandidates = @(
        'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
    )
    $RoslynCscPath = $roslynCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

$targets = @('de4dot')
if ($Clean.IsPresent) {
    $targets = @('Clean', 'de4dot')
}

$msbuildArgs = @(
    $SolutionPath,
    "/t:$($targets -join ';')",
    '/p:Configuration=Release',
    '/p:Platform=Mixed Platforms',
    '/nologo'
)

if (-not [string]::IsNullOrWhiteSpace($RoslynCscPath)) {
    $roslynDir = Split-Path -Parent $RoslynCscPath
    $msbuildArgs += "/p:CscToolPath=$roslynDir"
    $msbuildArgs += '/p:CscToolExe=csc.exe'
}

Write-Host "Using MSBuild: $MsBuildPath"
Write-Host "Solution: $SolutionPath"
Write-Host "Targets: $($targets -join ', ')"
Write-Host 'Configuration: Release | Mixed Platforms'
if (-not [string]::IsNullOrWhiteSpace($RoslynCscPath)) {
    Write-Host "C# compiler: $RoslynCscPath"
}
else {
    Write-Host 'C# compiler: default (legacy csc.exe)'
}
Write-Host ''

& $MsBuildPath @msbuildArgs
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $repoRoot 'Release\de4dot.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build succeeded but de4dot.exe is missing: $exePath"
}

$exe = Get-Item -LiteralPath $exePath
Write-Host ''
Write-Host "Build OK: $($exe.FullName)"
Write-Host "LastWriteTime: $($exe.LastWriteTime)"
