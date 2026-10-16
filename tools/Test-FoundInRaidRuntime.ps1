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
 $case.upd.SpawnedInSession=$true
 $firAmmo=Item 'aaaaaaaaaaaaaaaaaaaaaaa2' 20 $inv.stash 9 $true
 $inv.items+=@($case,$child,$ammo,$firAmmo)
 $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
 @{Schema=1;Revision=0;Items=@();History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 Start-Test
 $s=Read-Spt $a
 $null=Send 'deposit' $ammo._id $s.revision 10
 $s=Read-Spt $a;Check ($s.items.Count -eq 1) 'Default rules reject non-FIR'
 Stop-Test
 $c=Get-Content $configPath -Raw | ConvertFrom-Json -Depth 100
 $c.CommunityWarehouse | Add-Member FoundInRaidOnly $true -Force
 $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
 Start-Test
 $s=Read-Spt $a
 Check ($s.items.Count -eq 1) 'Enabling FIR removed existing items'
 Check (!($s.personal | Where-Object id -eq $ammo._id).transferable) 'Non-FIR UI still enabled'
 Check (($s.personal | Where-Object id -eq $firAmmo._id).transferable) 'FIR item UI disabled'
 Check (!($s.personal | Where-Object id -eq $case._id).transferable) 'Non-FIR child bypassed policy'
 foreach($amount in @($null,2)){
  $null=Send 'deposit' $ammo._id $s.revision $amount
  Check ((Read-Spt $a).revision -eq $s.revision) 'Non-FIR deposit accepted'
 }
 $null=Send 'deposit' $case._id $s.revision
 Check ((Read-Spt $a).revision -eq $s.revision) 'Container bypass accepted'
 $stored=$s.items[0]
 $null=Merge $a $ammo._id $true $stored.id $false $s.revision 1
 Check ((Read-Spt $a).revision -eq $s.revision) 'Non-FIR merge deposit accepted'
 $null=Merge $a $stored.id $false $ammo._id $true $s.revision 2
 $s=Read-Spt $a;Check ($s.items[0].count -eq 8) 'Non-FIR withdrawal merge blocked'
 $null=Send 'withdraw' $stored.id $s.revision 3
 $s=Read-Spt $a;Check ($s.items[0].count -eq 5) 'Non-FIR partial withdrawal blocked'
 $null=Send 'withdraw' $stored.id $s.revision
 $s=Read-Spt $a;Check ($s.items.Count -eq 0) 'Non-FIR full withdrawal blocked'
 $null=Send 'deposit' $firAmmo._id $s.revision 5
 $s=Read-Spt $a;Check ($s.items.Count -eq 1 -and $s.items[0].count -eq 5) 'FIR partial deposit failed'
 $null=Merge $a $firAmmo._id $true $s.items[0].id $false $s.revision 2
 $s=Read-Spt $a;Check ($s.items[0].count -eq 7) 'FIR merge deposit failed'
 Stop-Test
 $c.CommunityWarehouse.FoundInRaidOnly=$false
 $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
 Start-Test
 $s=Read-Spt $a
 $null=Send 'deposit' $ammo._id $s.revision
 Check ((Read-Spt $a).revision -gt $s.revision) 'Disabling FIR did not restore deposits'
 'PASS: FIR policy default, restart toggle, UI flags, container descendants, full/split/merge restrictions and unrestricted withdrawals'
}finally{
 if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
