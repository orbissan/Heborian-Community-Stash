param([string]$SptRoot = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
$version = & (Join-Path $PSScriptRoot 'tools/Get-Version.ps1')
$SptRoot = (Resolve-Path -LiteralPath $SptRoot).Path
$runtime = Join-Path $SptRoot 'SPT_Runtime'
if ([Environment]::Version.Major -ne 10) { throw 'Verification requires PowerShell 7.6 on .NET 10.' }
$refs = @(Get-ChildItem "$PSHOME/ref/*.dll" | Select-Object -ExpandProperty FullName)
foreach ($name in @('SemanticVersioning','SPTarkov.Common','SPTarkov.DI','SPTarkov.Server.Core')) {
    $path = Join-Path $runtime "$name.dll"
    [Reflection.Assembly]::LoadFrom($path) | Out-Null
    $refs += $path
}
$mod = Join-Path $PSScriptRoot "artifacts/$version/SPT_Runtime/user/mods/Heborian/Heborian.dll"
[Reflection.Assembly]::LoadFrom($mod) | Out-Null
$refs += $mod
$feeOptions=[Heborian.Server.Modules.CommunityWarehouse.WarehouseFeeOptions]::new()
if($feeOptions.Enabled -or $feeOptions.Currency -ne 'RUB'){throw 'Unexpected fee defaults'}
foreach($currency in @('RUB','EUR','USD')){$feeOptions.Currency=$currency;$feeOptions.Deposit=100000;$feeOptions.Validate()}
$feeOptions.Currency='BTC';$rejected=$false
try{$feeOptions.Validate()}catch{$rejected=$true}
if(!$rejected){throw 'Invalid fee currency accepted'}
$feeOptions.Currency='RUB';$feeOptions.Deposit=-1;$rejected=$false
try{$feeOptions.Validate()}catch{$rejected=$true}
if(!$rejected){throw 'Negative fee accepted'}
Write-Output 'PASS: fee defaults, supported currencies, large prices and invalid settings.'

$files = @(Get-ChildItem (Join-Path $runtime 'SPT_Data/database/locales/global') -Filter '*.json')
$before = @{}
foreach ($file in $files) { $before[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
Add-Type -Path (Join-Path $PSScriptRoot 'tests/LocaleVerification.cs'), (Join-Path $PSScriptRoot 'tests/ServerVerification.cs'), (Join-Path $PSScriptRoot 'tests/WarehouseVerification.cs') -ReferencedAssemblies $refs
[LocaleVerification]::Run($runtime)
[ServerVerification]::Run((Join-Path $PSScriptRoot 'test-results/config'))
[WarehouseVerification]::Run((Join-Path $PSScriptRoot 'test-results/warehouse'))
Add-Type -Path (Join-Path $PSScriptRoot 'tests/AuditStorageVerification.cs') -ReferencedAssemblies $refs
[AuditStorageVerification]::Run((Join-Path $PSScriptRoot 'test-results/audit-storage'))
Add-Type -Path (Join-Path $PSScriptRoot 'tests/WarehouseHistoryVerification.cs') -ReferencedAssemblies $refs
[WarehouseHistoryVerification]::Run((Join-Path $PSScriptRoot 'test-results/history'))
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne $before[$file.FullName]) { throw "Database file changed: $($file.Name)" }
}
Write-Output 'PASS: original locale files unchanged (SHA-256).'
& "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tools/Test-Client.ps1') -SptRoot $SptRoot
if ($LASTEXITCODE -ne 0) { throw 'Client verification failed.' }
Add-Type -Path (Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll')
$client = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot "artifacts/$version/BepInEx/plugins/Heborian/Heborian.Client.dll"))
try {
    if ($client.MainModule.AssemblyReferences | Where-Object { $_.Name -match 'SPTarkov.Server|System.Private.CoreLib' -or ($_.Name -eq 'System.Runtime' -and $_.Version.Major -ge 10) }) { throw 'Client references server/.NET10 runtime.' }
    $plugin = $client.MainModule.GetType('Heborian.Client.HeborianPlugin')
    if ($plugin.BaseType.FullName -ne 'BepInEx.BaseUnityPlugin') { throw 'Wrong plugin base class.' }
    $dependency = $plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' }
    if ($dependency.ConstructorArguments[0].Value -ne 'com.SPT.core' -or $dependency.ConstructorArguments[1].Value -ne '4.1.6') { throw 'SPT dependency missing.' }
    Write-Output 'PASS: BepInEx plugin metadata and Mono references; no server/.NET10 client dependency.'
} finally { $client.Dispose() }
& (Join-Path $PSScriptRoot 'tools/Test-MenuBindings.ps1') -SptRoot $SptRoot
# Isolate original game assemblies from the server test runtime.
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'tools/Test-NativeDeletion.ps1') -SptRoot $SptRoot
if ($LASTEXITCODE -ne 0) { throw 'Native deletion regression failed.' }
if ([Reflection.AssemblyName]::GetAssemblyName($mod).Version.ToString(3) -ne $version) { throw 'Server assembly version differs from banner version.' }
$clientPath = Join-Path $PSScriptRoot "artifacts/$version/BepInEx/plugins/Heborian/Heborian.Client.dll"
if ([Reflection.AssemblyName]::GetAssemblyName($clientPath).Version.ToString(3) -ne $version) { throw 'Client assembly version differs from banner version.' }
Write-Output "PASS: server/client assemblies and banner share version $version."
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot "dist/Heborian-$version-SPT-4.1.6.zip"))
try {
    $dlls = @($zip.Entries | Where-Object { $_.FullName -like '*.dll' } | ForEach-Object { $_.FullName })
    if (!$zip.GetEntry('SPT_Runtime/user/mods/Heborian/CONFIGURATION.md') -or !$zip.GetEntry('SPT_Runtime/user/mods/Heborian/HISTORY.md')) { throw 'Installed configuration documentation missing.' }
    if ($zip.Entries | Where-Object { $_.Name -eq 'config.example.json' }) { throw 'Obsolete example config in install package.' }
    if ($dlls.Count -ne 2 -or $dlls -notcontains 'SPT_Runtime/user/mods/Heborian/Heborian.dll' -or $dlls -notcontains 'BepInEx/plugins/Heborian/Heborian.Client.dll') { throw 'Unexpected DLLs in install package.' }
    if ($zip.Entries | Where-Object { $_.FullName -match '(^|/)config.json$|\.cfg$|EscapeFromTarkov_Data' }) { throw 'Package would overwrite user config/original files.' }
    Write-Output 'PASS: install package paths, both DLLs, no original binaries or user config replacements.'
} finally { $zip.Dispose() }
