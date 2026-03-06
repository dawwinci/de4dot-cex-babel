[CmdletBinding()]
param(
    [string]$WorkspaceRoot,
    [string]$De4dotPath,
    [string]$OutputCsv,
    [switch]$IncludeDerived
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($scriptDir)) {
    $scriptDir = (Get-Location).Path
}
if ([string]::IsNullOrEmpty($WorkspaceRoot)) {
    $WorkspaceRoot = Split-Path (Split-Path $scriptDir -Parent) -Parent
}
if ([string]::IsNullOrEmpty($De4dotPath)) {
    $De4dotPath = Join-Path (Split-Path $scriptDir -Parent) 'Release\de4dot.exe'
}
if ([string]::IsNullOrEmpty($OutputCsv)) {
    $OutputCsv = Join-Path $scriptDir 'babel-regression-results.csv'
}

function Invoke-De4dot {
    param(
        [Parameter(Mandatory = $true)][string[]]$ArgsList
    )

    $env:VisualStudioDir = '1'
    $all = & $De4dotPath @ArgsList 2>&1
    $text = ($all | Out-String)
    [PSCustomObject]@{
        ExitCode = $LASTEXITCODE
        Output   = $text
    }
}

function Parse-BabelScore {
    param([string]$Text)
    $m = [regex]::Match($Text, '(?m)^\s*(\d+):\s+Babel \.NET(?:\s+\S+)?\s*$')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return $null
}

function Parse-DetectedName {
    param([string]$Text)
    $m = [regex]::Match($Text, '(?m)^Detected\s+(.+?)\s+\(')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return 'Unknown'
}

function Parse-BabelVersion {
    param([string]$Text)

    $m = [regex]::Match($Text, '(?m)^\[\+\]\s+Babel version:\s+([0-9]+(?:\.[0-9]+){1,3})\s*$')
    if ($m.Success) { return $m.Groups[1].Value }

    $mH = [regex]::Match($Text, '(?m)^\[\*\]\s+Babel version \(assembly heuristic\):\s+([0-9]+(?:\.[0-9]+){1,3})\s*$')
    if ($mH.Success) { return ($mH.Groups[1].Value + ' (heuristic)') }

    $m2 = [regex]::Match($Text, '(?m)^Detected\s+Babel \.NET\s+([0-9]+(?:\.[0-9]+){1,3})\s+\(')
    if ($m2.Success) { return $m2.Groups[1].Value }

    return 'unknown'
}

function Parse-UnresolvedDelegates {
    param([string]$Text)
    $m = [regex]::Match($Text, '(?m)^\[\!\]\s+Babel delegate cleanup incomplete:\s+(\d+)\s+unresolved wrappers remain\s*$')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return 0
}

function Parse-MissingDependencies {
    param([string]$Text)
    $m = [regex]::Match($Text, '(?m)^\[\!\]\s+Missing runtime dependencies observed:\s+(.+?)\s*$')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

if (-not (Test-Path -LiteralPath $De4dotPath)) {
    throw "de4dot not found at: $De4dotPath"
}

$testDirs = Get-ChildItem -Path $WorkspaceRoot -Directory | Where-Object { $_.Name -like 'test_file*' }
if (-not $testDirs) {
    throw "No test directories matching 'test_file*' found in: $WorkspaceRoot"
}

$inputs = foreach ($dir in $testDirs) {
    Get-ChildItem -Path $dir.FullName -File -Filter *.dll
}

if (-not $IncludeDerived) {
    $inputs = $inputs | Where-Object {
        $_.Name -notmatch '(?i)(cleaned|deobfuscated|_autoclean|_2step|_out)'
    }
}

$inputs = $inputs | Sort-Object FullName -Unique

if (-not $inputs) {
    throw 'No DLL inputs selected after filtering.'
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($dll in $inputs) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($dll.Name)
    $outFile = Join-Path $dll.DirectoryName ($baseName + '_autoclean.dll')

    $detect1 = Invoke-De4dot -ArgsList @('-d', '-f', $dll.FullName, '-v')
    $initialScore = Parse-BabelScore $detect1.Output
    $initialDetected = Parse-DetectedName $detect1.Output

    $clean = Invoke-De4dot -ArgsList @('-f', $dll.FullName, '-o', $outFile, '-v')
    $babelVersion = Parse-BabelVersion $clean.Output
    $unresolvedDelegates = Parse-UnresolvedDelegates $clean.Output
    $missingDeps = Parse-MissingDependencies $clean.Output

    $postDetected = 'N/A'
    $hash = ''
    if (Test-Path -LiteralPath $outFile) {
        $detect2 = Invoke-De4dot -ArgsList @('-d', '-f', $outFile, '-v')
        $postDetected = Parse-DetectedName $detect2.Output
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $outFile).Hash
    }

    $results.Add([PSCustomObject]@{
        InputFile          = $dll.FullName
        InitialDetect      = $initialDetected
        InitialBabelScore  = if ($null -eq $initialScore) { '' } else { $initialScore }
        BabelVersion       = $babelVersion
        CleanupExitCode    = $clean.ExitCode
        UnresolvedDelegates = $unresolvedDelegates
        MissingDependencies = $missingDeps
        DetectAfterCleanup = $postDetected
        OutputHashSHA256   = $hash
        OutputFile         = $outFile
    }) | Out-Null
}

$results | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8
$results | Format-Table -AutoSize
"`nSaved CSV: $OutputCsv"
