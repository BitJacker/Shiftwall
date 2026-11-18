<#
.SYNOPSIS
  Measures Shiftwall start-up time: time from process start until the main window exists.
.DESCRIPTION
  The first run is reported separately (the single-file host extracts native runtime files on first start);
  the median of the following runs is the warm start-up time. Used by CI; no network access.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $Runs = 7,
    [string] $Label = (Split-Path (Split-Path $Exe -Parent) -Leaf)
)
$ErrorActionPreference = 'Stop'

function Measure-Once([string] $path) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $path -PassThru
    while ($p.MainWindowHandle -eq 0 -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 20; $p.Refresh() }
    $ms = [int]$sw.Elapsed.TotalMilliseconds
    if ($p.MainWindowHandle -eq 0) { Stop-Process $p -ErrorAction SilentlyContinue; throw "No main window within 60 s ($path)" }
    Stop-Process $p
    $p.WaitForExit()
    Start-Sleep -Milliseconds 500
    return $ms
}

$first = Measure-Once $Exe
$warm = @(for ($i = 0; $i -lt $Runs; $i++) { Measure-Once $Exe }) | Sort-Object
$median = $warm[[int][Math]::Floor($warm.Count / 2)]
$sizeMb = [Math]::Round((Get-Item $Exe).Length / 1MB, 1)
[pscustomobject]@{ Variant = $Label; SizeMB = $sizeMb; FirstRunMs = $first; WarmMedianMs = $median; WarmRunsMs = ($warm -join ' ') }
