param([string]$SptRoot = (Join-Path $PSScriptRoot '..'), [switch]$UseBundledCompiler)
$ErrorActionPreference = 'Stop'
$SptRoot = (Resolve-Path -LiteralPath $SptRoot).Path
$version = & (Join-Path $PSScriptRoot 'tools/Get-Version.ps1')
$stage = Join-Path $PSScriptRoot "artifacts/$version"
$serverOutput = Join-Path $stage 'SPT_Runtime/user/mods/Heborian'
$clientOutput = Join-Path $stage 'BepInEx/plugins/Heborian'
$runtime = Join-Path $SptRoot 'SPT_Runtime'
$managed = Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed'
foreach ($folder in @($serverOutput, $clientOutput)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
if (([Reflection.AssemblyName]::GetAssemblyName((Join-Path $SptRoot 'BepInEx/plugins/spt/spt-core.dll'))).Version.ToString() -ne '4.1.6.0') { throw 'Build requires installed SPT client 4.1.6.' }
$bepVersion = ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $SptRoot 'BepInEx/core/BepInEx.dll'))).Version
if ($bepVersion.Major -ne 5) { throw 'Build requires BepInEx 5 (Unity Mono).' }
Write-Output "References: installed SPT 4.1.6, BepInEx $bepVersion"
$sdk = if (Get-Command dotnet -ErrorAction SilentlyContinue) { @(dotnet --list-sdks 2>$null) } else { @() }
if (!$UseBundledCompiler -and ($sdk | Where-Object { $_ -match '^10\.' })) {
    foreach ($part in @('Server', 'Client')) {
        $destination = Join-Path $PSScriptRoot "bin/$part"
        dotnet build (Join-Path $PSScriptRoot "src/Heborian.$part/Heborian.$part.csproj") -c Release "-p:SptRoot=$SptRoot" -o $destination
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $part" }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Server/Heborian.dll') -Destination $serverOutput -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Client/Heborian.Client.dll') -Destination $clientOutput -Force
} else {
    # The compiler host uses .NET 10; client references MUST remain Unity/Mono assemblies.
    if ([Environment]::Version.Major -ne 10 -or !(Test-Path "$PSHOME/ref/System.Runtime.dll")) { throw 'Use .NET 10 SDK or PowerShell 7.6 (.NET 10).' }
    Add-Type -Path "$PSHOME/Microsoft.CodeAnalysis.dll"
    Add-Type -Path "$PSHOME/Microsoft.CodeAnalysis.CSharp.dll"
    $helperRefs = @(Get-ChildItem "$PSHOME/ref/*.dll" | Select-Object -ExpandProperty FullName)
    $helperRefs += "$PSHOME/Microsoft.CodeAnalysis.dll", "$PSHOME/Microsoft.CodeAnalysis.CSharp.dll"
    Add-Type -Path (Join-Path $PSScriptRoot 'tools/Compiler.cs') -ReferencedAssemblies $helperRefs -CompilerOptions '/nowarn:1701'
    $serverRefs = @(Get-ChildItem "$PSHOME/ref/*.dll" | Select-Object -ExpandProperty FullName)
    foreach ($name in @('SPTarkov.Server.Core', 'SPTarkov.DI', 'SPTarkov.Common', 'SemanticVersioning')) { $serverRefs += Join-Path $runtime "$name.dll" }
    $clientRefs = @('mscorlib','System','System.Core','netstandard','UnityEngine','UnityEngine.CoreModule','UnityEngine.ImageConversionModule','UnityEngine.UI','UnityEngine.UIModule','UnityEngine.InputLegacyModule','Unity.TextMeshPro','Assembly-CSharp','Comfort','Newtonsoft.Json') | ForEach-Object { Join-Path $managed "$_.dll" }
    $clientRefs += Join-Path $SptRoot 'BepInEx/plugins/spt/spt-common.dll'
    $clientRefs += Join-Path $SptRoot 'BepInEx/core/BepInEx.dll'
    $clientRefs += Join-Path $SptRoot 'BepInEx/core/0Harmony.dll'
    $clientRefs += Join-Path $managed 'Sirenix.Serialization.dll'
    $serverSources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src/Heborian.Server') -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | Select-Object -ExpandProperty FullName)
    $clientSources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src/Heborian.Client') -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | Select-Object -ExpandProperty FullName)
    $shared = @(Get-ChildItem (Join-Path $PSScriptRoot 'src/Shared') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    [HeborianBuild.Compiler]::Compile(($serverSources + $shared), $serverRefs, (Join-Path $serverOutput 'Heborian.dll'), '.NETCoreApp,Version=v10.0', $version)
    [HeborianBuild.Compiler]::Compile(($clientSources + $shared), $clientRefs, (Join-Path $clientOutput 'Heborian.Client.dll'), '.NETStandard,Version=v2.1', $version)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config/server/config.example.json') -Destination $serverOutput -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $clientOutput -Recurse -Force
foreach ($destination in @($serverOutput, $clientOutput)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.txt'), (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') -Destination $destination -Force
}
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
function New-Package([string]$Path, [string]$Base, [string[]]$Files) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $Files) {
            $entry = $zip.CreateEntry($relative.Replace('\','/'))
            $target = $entry.Open()
            $source = [IO.File]::OpenRead((Join-Path $Base $relative))
            try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
# Whitelist excludes test data, proprietary libraries and generated user configurations.
$installFiles = @(
    'SPT_Runtime/user/mods/Heborian/Heborian.dll',
    'SPT_Runtime/user/mods/Heborian/config.example.json',
    'SPT_Runtime/user/mods/Heborian/README.txt',
    'SPT_Runtime/user/mods/Heborian/LICENSE',
    'SPT_Runtime/user/mods/Heborian/THIRD-PARTY-NOTICES.txt',
    'BepInEx/plugins/Heborian/Heborian.Client.dll',
    'BepInEx/plugins/Heborian/assets/README.txt',
    'BepInEx/plugins/Heborian/assets/HeborianMenu/icon_heborian_01.png',
    'BepInEx/plugins/Heborian/assets/CommunityWarehouse/cell_full_border.png',
    'BepInEx/plugins/Heborian/README.txt',
    'BepInEx/plugins/Heborian/LICENSE',
    'BepInEx/plugins/Heborian/THIRD-PARTY-NOTICES.txt'
)
New-Package (Join-Path $dist "Heborian-$version-SPT-4.1.6.zip") $stage $installFiles
$sourceFiles = @('Heborian.slnx','Directory.Build.props','build.ps1','verify.ps1','README.md','README.txt','CONTRIBUTING.md','.gitignore','.gitattributes','LICENSE','THIRD-PARTY-NOTICES.txt','VALIDATION.txt')
foreach ($folder in @('src','tests','tools','docs','config','assets','.github')) {
    $sourceFiles += Get-ChildItem (Join-Path $PSScriptRoot $folder) -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | ForEach-Object { [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName) }
}
New-Package (Join-Path $dist "Heborian-$version-source.zip") $PSScriptRoot $sourceFiles
Write-Output "Built both assemblies and ZIPs in $dist"


