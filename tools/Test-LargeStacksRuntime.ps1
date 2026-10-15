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
 $inv=$p.characters.pmc.Inventory
 $inv.items=@($inv.items | Where-Object slotId -ne 'hideout')
 $cases=@(
 @('5449016a4bdc2d6f028b456f',1000000,123457),
 @('5696686a4bdc2da3298b456a',50000,12345),
 @('569668774bdc2da2298b4568',50000,23456),
 @('56dff061d2720bb5668b4567',60,17)
 )
 for($i=0;$i -lt $cases.Count;$i++){
  $item=Item ('aaaaaaaaaaaaaaaaaaaaaaa'+($i+1)) $cases[$i][1] $inv.stash $i
  $item._tpl=$cases[$i][0];$inv.items+=@($item)
 }
 $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
 @{Schema=1;Revision=0;Items=@();History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 Start-Test
 for($i=0;$i -lt $cases.Count;$i++){
  $case=$cases[$i];$id='aaaaaaaaaaaaaaaaaaaaaaa'+($i+1)
  $s=Read-Spt $a;Check (!$s.error) $s.error
  $null=Send 'deposit' $id $s.revision $case[2]
  $s=Read-Spt $a
  $shared=$s.items | Where-Object template -eq $case[0]
  Check ($shared.count -eq $case[2] -and ($s.personal | Where-Object id -eq $id).count -eq ($case[1]-$case[2])) 'Partial deposit quantity incorrect'
  $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='move';itemId=$shared.id;revision=$s.revision;x=$i;y=2;amount=7}
  $null=Read-Spt $a (@{data=@($op)}|ConvertTo-Json -Depth 8 -Compress)
  $s=Read-Spt $a
  Check (@($s.items | Where-Object template -eq $case[0]).Count -eq 2) 'Shared split failed'
  $null=Send 'withdraw' $shared.id $s.revision 3
  $s=Read-Spt $a
  $total=(@($s.items)+@($s.personal) | Where-Object template -eq $case[0] | Measure-Object count -Sum).Sum
  Check ($total -eq $case[1]) 'Partial transfer lost or duplicated quantity'
  "PASS: $($case[0]) count=$($case[1]), exact partial deposit=$($case[2]), shared split and partial withdrawal conserved quantity"
 }
}finally{
 if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
