param([string]$SptRoot)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$version = & (Join-Path $PSScriptRoot 'Get-Version.ps1')
$managed = Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed'
$references = @('System.dll','System.Core.dll')
foreach ($name in @('netstandard','UnityEngine','UnityEngine.CoreModule','UnityEngine.ImageConversionModule','Newtonsoft.Json')) {
    $path = Join-Path $managed "$name.dll"
    [Reflection.Assembly]::LoadFrom($path) | Out-Null
    $references += $path
}
foreach ($path in @((Join-Path $SptRoot 'BepInEx/core/BepInEx.dll'), (Join-Path $project "artifacts/$version/BepInEx/plugins/Heborian/Heborian.Client.dll"))) {
    [Reflection.Assembly]::LoadFrom($path) | Out-Null
    $references += $path
}
Add-Type -Path (Join-Path $project 'tests/ClientVerification.cs') -ReferencedAssemblies $references -IgnoreWarnings
[ClientVerification]::Run((Join-Path $project ('test-results/client-' + [Guid]::NewGuid().ToString('N'))))
