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
function ConfigureRows($rows) {
 $c=$originalConfig | ConvertFrom-Json -Depth 100
 $c.CommunityWarehouse | Add-Member Rows $rows -Force
 $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
}
try {
 $one=Item 'ccccccccccccccccccccccc1' 10 'warehouse' 15
 $one.location.y=31
 $two=Item 'ccccccccccccccccccccccc2' 1 'warehouse' 0
 $two._tpl='5448fee04bdc2dbc018b4567';$two.location.y=1
 $entries=@(
 @{Id=$one._id;Template=$one._tpl;Name='Ammo';Json=($one|ConvertTo-Json -Depth 10 -Compress);X=15;Y=31;Width=1;Height=1;Count=10},
 @{Id=$two._id;Template=$two._tpl;Name='Water';Json=($two|ConvertTo-Json -Depth 10 -Compress);X=0;Y=1;Width=1;Height=2;Count=1}
 )
 @{Schema=1;Revision=0;Items=$entries;History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 ConfigureRows 2
 Start-Test
 $s=Read-Spt $a;Check (!$s.error) $s.error
 Check ($s.width -eq 16 -and $s.height -eq 2 -and $s.items.Count -eq 0) 'Shrunken snapshot exposes hidden items or wrong size'
 Check ($s.blocked.Count -eq 1 -and $s.blocked[0].y -eq 1 -and $s.blocked[0].height -eq 1) 'Partial hidden footprint not reserved'
 $null=Send 'withdraw' $one._id $s.revision
 Check ((Read-Spt $a).revision -eq $s.revision) 'Hidden item could be withdrawn'
 $candidate=$s.personal | Where-Object { $_.transferable -and $_.height -le 2 } | Select-Object -First 1
 Check ($candidate) 'No eligible fixture item'
 $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='deposit';itemId=$candidate.id;revision=$s.revision;x=1;y=2}
 $null=Read-Spt $a (@{data=@($op)}|ConvertTo-Json -Depth 8 -Compress)
 Check ((Read-Spt $a).revision -eq $s.revision) 'Deposit beyond configured rows accepted'
 $null=Send 'deposit' $candidate.id $s.revision
 $s=Read-Spt $a;Check ($s.items.Count -eq 1) 'Visible deposit failed'
 Stop-Test
 $persisted=Get-Content $statePath -Raw | ConvertFrom-Json -Depth 100
 Check ($persisted.Items.Count -eq 3) 'Hidden items lost after visible write'
 ConfigureRows 32
 Start-Test
 $s=Read-Spt $a;Check (!$s.error) $s.error
 Check ($s.height -eq 32 -and $s.items.Count -eq 3 -and $s.blocked.Count -eq 0) 'Growing failed to reveal items'
 Check (($s.items | Where-Object id -eq $one._id).y -eq 31) 'Hidden item position changed'
 Check (($s.items | Where-Object id -eq $one._id).count -eq 10) 'Hidden item state changed'
 'PASS: 16x2 snapshot, hidden-item access rejected, boundary reservation, deposit bounds, persistence across writes and restoration to 16x32'
}finally{
 if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
