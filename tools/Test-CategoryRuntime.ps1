param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime')
$ErrorActionPreference='Stop'
$TestRoot=(Resolve-Path -LiteralPath $TestRoot).Path
if($TestRoot -ne 'H:\SPT New\Heborian\test-results\warehouse-runtime'){throw 'Unexpected test root'}
$a='691272fcd5f4c07b9cdec31c';$b='111111111111111111111111'
$statePath="$TestRoot/user/mods/Heborian/data/warehouse.json"
$originalState=[IO.File]::ReadAllText($statePath)
$originalProfiles=@{}
foreach($id in @($a,$b)){$originalProfiles[$id]=[IO.File]::ReadAllText("$TestRoot/user/profiles/$id.json")}
function Read-Spt($id,$body=$null){
 $path=if($body){'/client/game/profile/items/moving'}else{'/heborian/warehouse'}
 $args=@{Uri="https://127.0.0.1:6975$path";SkipCertificateCheck=$true;Headers=@{Cookie="PHPSESSID=$id";requestcompressed='0'};TimeoutSec=15;OutFile="$TestRoot/merge-response.bin"}
 if($body){$args.Method='Post';$args.Body=$body;$args.ContentType='application/json'}
 Invoke-WebRequest @args | Out-Null
 $bytes=[IO.File]::ReadAllBytes("$TestRoot/merge-response.bin")
 if($bytes[0] -eq 120){$ms=[IO.MemoryStream]::new($bytes);$z=[IO.Compression.ZLibStream]::new($ms,[IO.Compression.CompressionMode]::Decompress);$r=[IO.StreamReader]::new($z);try{$text=$r.ReadToEnd()}finally{$r.Dispose();$ms.Dispose()}}else{$text=[Text.Encoding]::UTF8.GetString($bytes)}
 $text | ConvertFrom-Json -Depth 100
}
function Merge($profile,$source,$personal,$target,$targetPersonal,$revision,$amount=$null,$rid=[guid]::NewGuid().ToString('N')){
 $op=@{Action='HeborianWarehouse';requestId=$rid;direction=$(if($personal){'deposit'}else{'withdraw'});itemId=$source;targetId=$target;targetPersonal=$targetPersonal;revision=$revision}
 if($null -ne $amount){$op.amount=$amount}
 Read-Spt $profile (@{data=@($op)} | ConvertTo-Json -Depth 8 -Compress)
}
function Item($id,$count,$parent,$x,$fir=$false){
 @{_id=$id;_tpl='56dff061d2720bb5668b4567';parentId=$parent;slotId='hideout';location=@{x=$x;y=0;r=0;isSearched=$true};upd=@{StackObjectsCount=$count;SpawnedInSession=$fir}}
}
function Check($condition,$message){if(!$condition){throw $message}}
$proc=$null
$configPath="$TestRoot/user/mods/Heborian/config.json"
$originalConfig=[IO.File]::ReadAllText($configPath)
function Configure($enabled) {
 $c=$originalConfig | ConvertFrom-Json -Depth 100
 $flags=@{}; foreach($k in @('Weapons','Gear','Tools','Food','Medicine','Ammo','Barter','WeaponMods','Keys','Money','Containers','Other')) { $flags[$k]=$enabled }
 $c.CommunityWarehouse | Add-Member AllowedDeposits $flags -Force
 $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
}
function Send($direction,$id,$revision,$amount=$null){
 $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction=$direction;itemId=$id;revision=$revision}
 if($null -ne $amount){$op.amount=$amount}
 Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Depth 8 -Compress)
}
function Stop-Test { if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()} }
function Start-Test { $version=& "$PSScriptRoot/Get-Version.ps1"
 Copy-Item "H:\SPT New\Heborian\artifacts\$version\SPT_Runtime\user\mods\Heborian\Heborian.dll" "$TestRoot/user/mods/Heborian/Heborian.dll" -Force
 $script:proc=Start-Process "$TestRoot/SPT.Server.exe" -WorkingDirectory $TestRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TestRoot/merge.out.log" -RedirectStandardError "$TestRoot/merge.err.log"
 for($i=0;$i -lt 30;$i++){Start-Sleep -Seconds 1;if($proc.HasExited){throw 'Test server exited'};if((Get-Content "$TestRoot/merge.out.log" -Raw) -match 'https://127.0.0.1:6975'){break}}

}
try {
 Configure $true
 $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
 $p.characters.pmc.InsuredItems=@()
 $inv=$p.characters.pmc.Inventory
 # Keep structural inventory objects; remove existing stash roots and all their descendants.
 $remove=[Collections.Generic.HashSet[string]]::new()
 foreach($i in $inv.items){if($i.parentId -eq $inv.stash){$null=$remove.Add($i._id)}}
 do{$added=$false;foreach($i in $inv.items){if($remove.Contains($i.parentId) -and $remove.Add($i._id)){$added=$true}}}while($added)
 $inv.items=@($inv.items | Where-Object { !$remove.Contains($_._id) })
 $weapon=$inv.items | Where-Object slotId -eq 'FirstPrimaryWeapon' | Select-Object -First 1
 Check ($null -ne $weapon) 'Fixture lacks equipped weapon'
 $weapon.parentId=$inv.stash;$weapon.slotId='hideout'
 $weapon | Add-Member location @{x=0;y=0;r=0;isSearched=$true} -Force
 $case=@{_id='ddddddddddddddddddddddd1';_tpl='5857a8b324597729ab0a0e7d';parentId=$inv.stash;slotId='hideout';location=@{x=6;y=12;r=1;isSearched=$true};upd=@{StackObjectsCount=1}}
 $child=Item 'ddddddddddddddddddddddd2' 12 $case._id 0; $child.slotId='main'
 $ammo=Item 'aaaaaaaaaaaaaaaaaaaaaaa1' 40 $inv.stash 8
 $inv.items+=@($case,$child,$ammo)
 $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
 @{Schema=1;Revision=0;Items=@();History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 Start-Test
 $s=Read-Spt $a;Check (!$s.error) $s.error
 $weaponView=$s.personal | Where-Object id -eq $weapon._id
 Check ($weaponView.transferable -and $weaponView.tree.Count -gt 1) 'Assembled weapon not eligible with enabled categories'
 $weaponIds=@($weaponView.tree._id)
 $r=Send 'deposit' $weapon._id $s.revision
 Check (@($r.data.profileChanges.$a.items.del).Count -eq 1 -and $r.data.profileChanges.$a.items.del[0]._id -eq $weapon._id) 'Weapon client delta must delete only the root'
 $s=Read-Spt $a
 Check (($s.items | Where-Object id -eq $weapon._id).tree.Count -eq $weaponIds.Count) 'Weapon tree not stored'
 $null=Send 'deposit' $case._id $s.revision
 $s=Read-Spt $a
 Check (($s.items | Where-Object id -eq $case._id).tree.Count -eq 2) 'Container contents not stored'
 $null=Send 'deposit' $ammo._id $s.revision 20
 $s=Read-Spt $a
 Check ($s.items.Count -eq 3) 'Enabled deposits failed'
 'PASS: enabled categories accept complete weapon and container trees, profile deltas and partial ammunition'
 Stop-Test
 Configure $false
 Start-Test
 $s=Read-Spt $a
 Check ($s.items.Count -eq 3) 'Disabling categories removed warehouse contents'
 Check (!($s.personal | Where-Object id -eq $ammo._id).transferable) 'Disabled deposit still offered'
 $null=Send 'deposit' $ammo._id $s.revision
 Check ((Read-Spt $a).revision -eq $s.revision) 'Disabled deposit accepted'
 $storedAmmo=$s.items | Where-Object template -eq $ammo._tpl
 $null=Merge $a $ammo._id $true $storedAmmo.id $false $s.revision 1
 Check ((Read-Spt $a).revision -eq $s.revision) 'Disabled merge deposit accepted'
 $null=Merge $a $storedAmmo.id $false $ammo._id $true $s.revision 5
 $s=Read-Spt $a
 Check (($s.personal | Where-Object id -eq $ammo._id).count -eq 25) 'Disabled-category withdrawal merge failed'
 $null=Send 'withdraw' $storedAmmo.id $s.revision 5
 $s=Read-Spt $a
 Check (($s.items | Where-Object id -eq $storedAmmo.id).count -eq 10) 'Disabled-category partial withdrawal failed'
 foreach($id in @($weapon._id,$case._id,$storedAmmo.id)){
  $r=Send 'withdraw' $id $s.revision
  if($id -eq $weapon._id){Check (@($r.data.profileChanges.$a.items.new).Count -eq $weaponIds.Count) 'Weapon withdrawal delta omits attachments'}
  $s=Read-Spt $a;Check (!($s.items | Where-Object id -eq $id)) 'Disabled-category full withdrawal failed'
 }
 Check (($s.personal | Where-Object id -eq $weapon._id).tree.Count -eq $weaponIds.Count) 'Restored weapon lost parts'
 Check (($s.personal | Where-Object id -eq $case._id).tree.Count -eq 2) 'Restored container lost contents'
 'PASS: disabling categories persists items, blocks deposits/merge deposits, permits full/split/merge withdrawals with complete trees'
 Copy-Item $statePath "$TestRoot/categories-tested-state.json" -Force
 Stop-Test
 $history=(Get-Content "$TestRoot/categories-tested-state.json" -Raw | ConvertFrom-Json -Depth 100).History
 foreach($direction in @('deposit','withdraw')){
  $transfer=$history | Where-Object { $_.Direction -eq $direction -and $_.Entry.Id -eq $weapon._id } | Select-Object -First 1
  foreach($saved in @($false,$true)){
   $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
   foreach($change in $transfer.ProfileChanges){
    $p.characters.pmc.Inventory.items=@($p.characters.pmc.Inventory.items | Where-Object _id -ne $change.Id)
    $value=if($saved){$change.After}else{$change.Before}
    if($value){$p.characters.pmc.Inventory.items+=@($value | ConvertFrom-Json -Depth 100)}
   }
   $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
   $entries=if($direction -eq 'withdraw'){@($transfer.Entry)}else{@()}
   @{Schema=1;Revision=0;Items=@($entries);History=@();Pending=$transfer} | ConvertTo-Json -Depth 100 -Compress | Set-Content $statePath
   Start-Test
   $s=Read-Spt $a;Check (!$s.error) $s.error
   $state=Get-Content $statePath -Raw | ConvertFrom-Json -Depth 100
   $profile=Get-Content "$TestRoot/user/profiles/$a.json" -Raw | ConvertFrom-Json -Depth 100
   Check ($null -eq $state.Pending -and $state.Revision -eq 1) 'Tree recovery did not commit once'
   foreach($change in $transfer.ProfileChanges){
    $actual=@($profile.characters.pmc.Inventory.items | Where-Object _id -eq $change.Id)
    Check ($actual.Count -eq $(if($direction -eq 'deposit'){0}else{1})) 'Tree recovery lost or duplicated a part'
   }
   if($direction -eq 'deposit'){Check ($s.items[0].tree.Count -eq $weaponIds.Count) 'Recovered stored weapon incomplete'}
   Stop-Test
  }
 }
 'PASS: full weapon-tree journal recovery before/after profile save, deposit and withdrawal, with categories disabled'
}finally{
 if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
