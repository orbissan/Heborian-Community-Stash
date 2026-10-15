param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime')
$ErrorActionPreference='Stop'
$TestRoot=(Resolve-Path -LiteralPath $TestRoot).Path
if($TestRoot -ne 'H:\SPT New\Heborian\test-results\warehouse-runtime'){throw 'Unexpected test root'}
$statePath="$TestRoot/user/mods/Heborian/data/warehouse.json"
$originalState=[IO.File]::ReadAllText($statePath)
$history=(Get-Content "$TestRoot/merge-tested-state.json" -Raw | ConvertFrom-Json -Depth 100).History
$deposit=$history | Where-Object {$_.ProfileChanges.Count -eq 1 -and $null -eq $_.ProfileChanges[0].After} | Select-Object -First 1
$withdraw=$history | Where-Object {$_.ProfileChanges.Count -eq 1 -and $_.SharedUpdated.Count -eq 0} | Select-Object -First 1
$profilePath="$TestRoot/user/profiles/$($deposit.Profile).json"
$originalProfile=[IO.File]::ReadAllText($profilePath)
try{
 foreach($mode in @('deposit','withdraw')){foreach($saved in @($false,$true)){
  $transfer=if($mode -eq 'deposit'){$deposit}else{$withdraw}
  $p=$originalProfile | ConvertFrom-Json -Depth 100
  foreach($change in $transfer.ProfileChanges){
   $p.characters.pmc.Inventory.items=@($p.characters.pmc.Inventory.items | Where-Object _id -NE $change.Id)
   $value=if($saved){$change.After}else{$change.Before}
   if($value){$p.characters.pmc.Inventory.items+=@($value | ConvertFrom-Json -Depth 100)}
  }
  $entry=$deposit.SharedUpdated[0] | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
  if($mode -eq 'deposit'){$entry.Count=15;$item=$entry.Json | ConvertFrom-Json -Depth 100;$item.upd.StackObjectsCount=15;$entry.Json=$item | ConvertTo-Json -Depth 100 -Compress}
  @{Schema=1;Revision=0;Items=@($entry);History=@();Pending=$transfer} | ConvertTo-Json -Depth 100 -Compress | Set-Content $statePath
  $p | ConvertTo-Json -Depth 100 -Compress | Set-Content $profilePath
  @{Profile=$transfer.Profile;Item=$entry.Id;Shared=$(if($mode -eq 'deposit'){1}else{0});Personal=0} | ConvertTo-Json | Set-Content "$TestRoot/recovery-expected.json"
  & "$PSScriptRoot/Test-WarehouseRuntime.ps1" -TestRoot $TestRoot -RecoveryOnly | Out-Null
  $state=Get-Content $statePath -Raw | ConvertFrom-Json -Depth 100
  $p=Get-Content $profilePath -Raw | ConvertFrom-Json -Depth 100
  if($state.Pending -or $state.Revision -ne 1){throw 'Merge recovery not committed once'}
  foreach($change in $transfer.ProfileChanges){
   $actual=@($p.characters.pmc.Inventory.items | Where-Object _id -EQ $change.Id)
   if(!$change.After){if($actual.Count){throw 'Consumed merge source recovered twice'}}
   else{$expected=$change.After | ConvertFrom-Json -Depth 100;if($actual.Count -ne 1 -or $actual[0].upd.StackObjectsCount -ne $expected.upd.StackObjectsCount){throw 'Recovered target quantity incorrect'}}
  }
  if($mode -eq 'deposit' -and $state.Items[0].Count -ne 35){throw 'Recovered shared target quantity incorrect'}
  "PASS: merge $mode recovery, profile saved=$saved; exact counts/IDs and single commit"
 }}
}finally{
 [IO.File]::WriteAllText($statePath,$originalState)
 [IO.File]::WriteAllText($profilePath,$originalProfile)
}
