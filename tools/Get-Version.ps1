$versionSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Shared/BuildInfo.cs'))
if ($versionSource -notmatch 'const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'Missing Heborian version constant.' }
$matches[1]
