param(
    [ValidateRange(1, 16384)][int]$Size = 1024,
    [ValidateRange(1, 1000)][int]$Rounds = 5,
    [ValidateRange(1, 100)][int]$Repeats = 1,
    [int[]]$ConcurrentImages = @(1, 2, 4),
    [int[]]$ThreadsPerImage = @(1, 2, 4, 8, 0),
    [ValidateSet('encode', 'decode')][string[]]$Operations = @('encode', 'decode'),
    [ValidateSet('lossless', 'lossy')][string[]]$Coding = @('lossless', 'lossy')
)

$ErrorActionPreference = 'Stop'
$benchmarkDll = Join-Path $PSScriptRoot 'bin/Release/net10.0/JxlSharp.ConcurrencyBench.dll'
if (-not (Test-Path -LiteralPath $benchmarkDll)) { throw 'Build the Release benchmark first; see README.md.' }
if (@($ConcurrentImages | Where-Object { $_ -lt 1 -or $_ -gt 64 }).Count) { throw 'ConcurrentImages must be between 1 and 64.' }
if (@($ThreadsPerImage | Where-Object { $_ -lt 0 }).Count) { throw 'ThreadsPerImage must be nonnegative; zero selects auto.' }
$outputDirectory = Join-Path $PSScriptRoot '../../artifacts/profiles/concurrency'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$resultPath = Join-Path $outputDirectory ('matrix-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff') + '.jsonl')
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    for ($trial = 0; $trial -lt $Repeats; $trial++) {
        foreach ($operation in $Operations) {
            foreach ($mode in $Coding) {
                foreach ($concurrentCount in $ConcurrentImages) {
                    # Reverse alternate trials to make fixed ordering less influential.
                    $threadCounts = @($ThreadsPerImage)
                    if ($trial % 2) { [Array]::Reverse($threadCounts) }
                    foreach ($workerCount in $threadCounts) {
                        $lines = & dotnet $benchmarkDll $operation $mode $concurrentCount $workerCount $Size $Rounds
                        if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $operation $mode $concurrentCount $workerCount" }
                        foreach ($line in $lines) {
                            $row = $line | ConvertFrom-Json
                            $row | Add-Member -NotePropertyName trial -NotePropertyValue $trial
                            $json = $row | ConvertTo-Json -Compress
                            Add-Content -LiteralPath $resultPath -Value $json -Encoding utf8
                            Write-Output $json
                        }
                    }
                }
            }
        }
    }
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
Write-Host "Results: $resultPath"
