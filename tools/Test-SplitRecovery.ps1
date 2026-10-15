param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime')
$ErrorActionPreference='Stop'
$TestRoot=(Resolve-Path $TestRoot).Path
if($TestRoot -ne 'H:\SPT New\Heborian\test-results\warehouse-runtime'){throw 'Unexpected test root'}
$statePath=Join-Path $TestRoot 'user/mods/Heborian/data/warehouse.json'
$originalState=[IO.File]::ReadAllText($statePath)
$state=$originalState | ConvertFrom-Json -Depth 100
$deposit=@($state.History | Where-Object {$_.Direction -eq 'deposit' -and $_.ProfileBeforeJson})[-1]
$withdraw=@($state.History | Where-Object {$_.Direction -eq 'withdraw' -and $_.SharedRemainder})[-1]
if(!$deposit -or !$withdraw){throw 'Run partial transfer HTTP tests first'}
$profiles=@{}
foreach($id in @($deposit.Profile,$withdraw.Profile) | Select-Object -Unique){$profiles[$id]=[IO.File]::ReadAllText((Join-Path $TestRoot "user/profiles/$id.json"))}
try {
 foreach($direction in @('deposit','withdraw')) {
  foreach($alreadySaved in @($false,$true)) {
   $transfer=if($direction -eq 'deposit'){$deposit}else{$withdraw}
   $profile=$profiles[$transfer.Profile] | ConvertFrom-Json -Depth 100
   $inventory=$profile.characters.pmc.Inventory
   $pending=@{Schema=1;Revision=0;Items=@();History=@();Pending=$transfer}
   if($direction -eq 'deposit') {
    $before=$transfer.ProfileBeforeJson | ConvertFrom-Json
    $after=$transfer.ProfileAfterJson | ConvertFrom-Json
    # Explicitly replace just the split source; preserve all unrelated test inventory.
    $inventory.items=@($profile.characters.pmc.Inventory.items | Where-Object {$_._id -ne $before._id -and $_._id -ne $transfer.Entry.Id})
    $inventory.items+=@( $(if($alreadySaved){$after}else{$before}) )
   } else {
    $newItem=$transfer.ProfileItemJson | ConvertFrom-Json
    $inventory.items=@($inventory.items | Where-Object {$_._id -ne $newItem._id})
    if($alreadySaved){$inventory.items+=@($newItem)}
    $source=$transfer.SharedRemainder | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $source.Count+=$transfer.Entry.Count
    $sourceItem=$source.Json | ConvertFrom-Json -Depth 100
    $sourceItem.upd.StackObjectsCount=$source.Count
    $source.Json=$sourceItem | ConvertTo-Json -Depth 100 -Compress
    $pending.Items=@($source)
   }
   $profilePath=Join-Path $TestRoot "user/profiles/$($transfer.Profile).json"
   $profile | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $profilePath -Encoding utf8
   $pending | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $statePath -Encoding utf8
   @{Profile=$transfer.Profile;Item=$transfer.Entry.Id;Shared=$(if($direction -eq 'deposit'){1}else{0});Personal=$(if($direction -eq 'deposit'){0}else{1})} | ConvertTo-Json | Set-Content "$TestRoot/recovery-expected.json"
   & (Join-Path $PSScriptRoot 'Test-WarehouseRuntime.ps1') -TestRoot $TestRoot -RecoveryOnly | Out-Null
   $result=Get-Content $statePath -Raw | ConvertFrom-Json -Depth 100
   $resultProfile=Get-Content $profilePath -Raw | ConvertFrom-Json -Depth 100
   if($result.Pending -or $result.Revision -ne 1){throw 'Recovery did not commit exactly once'}
   if($direction -eq 'deposit') {
    $left=@($resultProfile.characters.pmc.Inventory.items | Where-Object _id -EQ $after._id)
    if($left.Count -ne 1 -or $left[0].upd.StackObjectsCount -ne $after.upd.StackObjectsCount -or $result.Items[0].Count -ne $transfer.Entry.Count){throw 'Deposit recovery lost/duplicated quantity'}
   } else {
    $received=@($resultProfile.characters.pmc.Inventory.items | Where-Object _id -EQ $newItem._id)
    if($received.Count -ne 1 -or $received[0].upd.StackObjectsCount -ne $newItem.upd.StackObjectsCount -or $result.Items[0].Count -ne $transfer.SharedRemainder.Count){throw 'Withdrawal recovery lost/duplicated quantity'}
   }
   "PASS: partial $direction recovery, profile already saved=$alreadySaved, exact quantities and IDs."
  }
 }
} finally {
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $profiles.Keys){[IO.File]::WriteAllText((Join-Path $TestRoot "user/profiles/$id.json"),$profiles[$id])}
}

