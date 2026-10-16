param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime',[string]$ModVersion)
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
function Start-Test { $version=if($ModVersion){$ModVersion}else{& "$PSScriptRoot/Get-Version.ps1"}
 Copy-Item "H:\SPT New\Heborian\artifacts\$version\SPT_Runtime\user\mods\Heborian\Heborian.dll" "$TestRoot/user/mods/Heborian/Heborian.dll" -Force
 $script:proc=Start-Process "$TestRoot/SPT.Server.exe" -WorkingDirectory $TestRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TestRoot/merge.out.log" -RedirectStandardError "$TestRoot/merge.err.log"
 for($i=0;$i -lt 30;$i++){Start-Sleep -Seconds 1;if($proc.HasExited){throw 'Test server exited'};if((Get-Content "$TestRoot/merge.out.log" -Raw) -match 'https://127.0.0.1:6975'){break}}

}
try {
 Configure $true
 $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
 $inv=$p.characters.pmc.Inventory
 $db=Get-Content "$TestRoot/SPT_Data/database/templates/items.json" -Raw | ConvertFrom-Json -AsHashtable -Depth 100
 $stash=$inv.items | Where-Object _id -eq $inv.stash
 $grid=$db[$stash._tpl]._props.Grids[0]._props
 $width=[int]$grid.cellsH; $height=[int]$grid.cellsV
 Check ($width -gt 0 -and $height -gt 0) 'Fixture stash dimensions unavailable'
 $p.characters.pmc.Bonuses=@($p.characters.pmc.Bonuses | Where-Object {$_.type -ne 'StashRows' -and $_.type -ne 30})+@(@{type='StashRows';value=2})
 # Keep equipment and its contents; replace only the copied stash tree.
 $removed=[Collections.Generic.HashSet[string]]::new()
 foreach($root in @($inv.items | Where-Object {$_.parentId -eq $inv.stash})) { $null=$removed.Add($root._id) }
 do { $more=$false; foreach($item in $inv.items){if($removed.Contains($item.parentId) -and $removed.Add($item._id)){$more=$true}} } while($more)
 $inv.items=@($inv.items | Where-Object {!$removed.Contains($_._id)})
 for($y=0;$y -lt $height;$y++){for($x=0;$x -lt $width;$x++){
  $item=Item (('abcd{0:x20}' -f ($y*$width+$x))) 1 $inv.stash $x
  $item.location.y=$y; $inv.items+=@($item)
 }}
 $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
 $one=Item 'ccccccccccccccccccccccc1' 10 $inv.stash 0
 $entry=@{Id=$one._id;Template=$one._tpl;Name='Ammo';Json=($one|ConvertTo-Json -Depth 10 -Compress);X=0;Y=0;Width=1;Height=1;Count=10}
 @{Schema=1;Revision=0;Items=@($entry);History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 Start-Test
 $s=Read-Spt $a; Check (!$s.error) $s.error
 Check ($s.personalHeight -eq $height+2) 'Fixture extra-row bonus not active'
 $response=Send withdraw $one._id $s.revision 3
 $response | ConvertTo-Json -Depth 10 -Compress | Set-Content "$TestRoot/audit-withdraw-response.json"
 $s=Read-Spt $a
 Check ($s.revision -eq 1 -and ($s.items | Where-Object id -eq $one._id).count -eq 7) 'Partial withdrawal cannot use empty bonus stash rows'
 $new=@($s.personal | Where-Object {$_.y -ge $height})
 Check ($new.Count -eq 1 -and $new[0].count -eq 3) 'Partial withdrawal did not land in bonus rows'
 $null=Send withdraw $one._id $s.revision
 $s=Read-Spt $a
 Check ($s.revision -eq 2 -and $s.items.Count -eq 0 -and ($s.personal | Where-Object id -eq $one._id).y -ge $height) 'Whole withdrawal cannot use empty bonus stash rows'
 'PASS: partial and whole withdrawal use real stash size including bonus rows; base area remains occupied'
}finally{
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}



