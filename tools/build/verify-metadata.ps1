<#
.SYNOPSIS
  Fails if Shiftwall.exe lacks the expected Windows version metadata (name, version, copyright, description).
#>
param([Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $Version)
$ErrorActionPreference = 'Stop'
$v = (Get-Item $Exe).VersionInfo
$checks = [ordered]@{
    'ProductName'     = $v.ProductName -eq 'Shiftwall'
    'FileDescription' = $v.FileDescription -eq 'Shiftwall'
    'CompanyName'     = $v.CompanyName -eq 'Shiftwall contributors'
    'LegalCopyright'  = $v.LegalCopyright -like 'Copyright (c) * Shiftwall contributors'
    'FileVersion'     = $v.FileVersion -eq "$Version.0"
    'ProductVersion'  = $v.ProductVersion -like "$Version*"
}
$v | Format-List ProductName, FileDescription, CompanyName, LegalCopyright, FileVersion, ProductVersion | Out-String | Write-Host
$failed = $checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object { $_.Key }
if ($failed) { throw "Version metadata check failed: $($failed -join ', ')" }
Write-Host 'Version metadata OK'
