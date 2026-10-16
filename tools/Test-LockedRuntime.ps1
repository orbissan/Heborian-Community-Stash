param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime',[switch]$ExpectBug)
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
function Send($direction,$id,$revision,$amount=$null,$profile=$a){
 $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction=$direction;itemId=$id;revision=$revision}
 if($null -ne $amount){$op.amount=$amount}
 Read-Spt $profile (@{data=@($op)} | ConvertTo-Json -Depth 8 -Compress)
}
function Stop-Test { if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()} }
function Start-Test { $version=& "$PSScriptRoot/Get-Version.ps1"
 Copy-Item "H:\SPT New\Heborian\artifacts\$version\SPT_Runtime\user\mods\Heborian\Heborian.dll" "$TestRoot/user/mods/Heborian/Heborian.dll" -Force
 $script:proc=Start-Process "$TestRoot/SPT.Server.exe" -WorkingDirectory $TestRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TestRoot/merge.out.log" -RedirectStandardError "$TestRoot/merge.err.log"
 for($i=0;$i -lt 30;$i++){Start-Sleep -Seconds 1;if($proc.HasExited){throw 'Test server exited'};if((Get-Content "$TestRoot/merge.out.log" -Raw) -match 'https://127.0.0.1:6975'){break}}

}
function Balance($tpl) {
 $p=Get-Content "$TestRoot/user/profiles/$a.json" -Raw | ConvertFrom-Json -Depth 100
 [long](($p.characters.pmc.Inventory.items | Where-Object { $_._tpl -eq $tpl -and $_._id -in @('aaaaaaaaaaaaaaaaaaaaaa01','bbbbbbbbbbbbbbbbbbbbbb01') } | Measure-Object -Property { $_.upd.StackObjectsCount } -Sum).Sum)
}
try {
 Configure $true
 $c=Get-Content $configPath -Raw | ConvertFrom-Json -Depth 100
 $c.CommunityWarehouse | Add-Member FoundInRaidOnly $false -Force
 $c.CommunityWarehouse | Add-Member TransferFees @{Enabled=$false;Currency='RUB';Deposit=100;Withdrawal=100} -Force
 $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
 $tpl='5449016a4bdc2d6f028b456f'
  $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
  $inv=$p.characters.pmc.Inventory;$p.characters.pmc.InsuredItems=@()
  $remove=[Collections.Generic.HashSet[string]]::new()
  foreach($i in $inv.items){if($i.parentId -eq $inv.stash){$null=$remove.Add($i._id)}}
  do{$added=$false;foreach($i in $inv.items){if($remove.Contains($i.parentId) -and $remove.Add($i._id)){$added=$true}}}while($added)
  $inv.items=@($inv.items | Where-Object {!$remove.Contains($_._id)})
  $ammo=Item 'dddddddddddddddddddddd01' 60 $inv.stash 0
  $target=Item 'dddddddddddddddddddddd02' 10 $inv.stash 1
  $wallet=Item 'dddddddddddddddddddddd03' 1 $inv.stash 2;$wallet._tpl='5783c43d2459774bbe137486'
  $loose=Item 'aaaaaaaaaaaaaaaaaaaaaa01' 40 $inv.stash 3;$loose._tpl=$tpl
  $cash=Item 'bbbbbbbbbbbbbbbbbbbbbb01' 960 $wallet._id 0;$cash._tpl=$tpl;$cash.slotId='main'
  $inv.items+=@($ammo,$target,$wallet,$loose,$cash)
  $fixture=$p | ConvertTo-Json -Depth 100 -Compress
  $fixture | Set-Content "$TestRoot/user/profiles/$a.json"
  @{Schema=1;Revision=0;Items=@();History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath

 # Store a free shared stack for both merge directions.
 $shared=Item 'eeeeeeeeeeeeeeeeeeeeee01' 10 $inv.stash 8
 $entry=@{Id=$shared._id;Template=$shared._tpl;Name='Ammo';Json=($shared|ConvertTo-Json -Depth 10 -Compress);X=8;Y=0;Width=1;Height=1;Count=10}
 @{Schema=1;Revision=0;Items=@($entry);History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 $ammo.upd.PinLockState=2
 $target.upd.PinLockState=2
 $cash.upd.PinLockState=2
 $p | ConvertTo-Json -Depth 100 | Set-Content "$TestRoot/user/profiles/$a.json"
 Start-Test
 $s=Read-Spt $a
 $null=Send 'deposit' $ammo._id $s.revision
 $after=Read-Spt $a
 if($ExpectBug) {
  Check ($after.revision -eq $s.revision+1) 'Expected old lock bypass was not reproduced'
  'REPRODUCED: locked personal stack can be deposited by version under audit'
 } else {
  Check ($after.revision -eq $s.revision) 'Locked full deposit bypassed lock'
  Check (!(($s.personal | Where-Object id -eq $ammo._id).transferable)) 'Snapshot offers locked deposit'
  Check (!(($s.personal | Where-Object id -eq $target._id).canReceive)) 'Snapshot offers locked merge target'
  $null=Send 'deposit' $ammo._id $s.revision 1
  Check ((Read-Spt $a).revision -eq $s.revision) 'Locked split deposit bypassed lock'
  $null=Merge $a $ammo._id $true $shared._id $false $s.revision 1
  Check ((Read-Spt $a).revision -eq $s.revision) 'Locked merge source bypassed lock'
  $null=Merge $a $shared._id $false $target._id $true $s.revision 1
  Check ((Read-Spt $a).revision -eq $s.revision) 'Locked merge destination bypassed lock'
  $null=Send 'deposit' $wallet._id $s.revision
  Check ((Read-Spt $a).revision -eq $s.revision) 'Container bypassed locked contents'
  $null=Send 'withdraw' $shared._id $s.revision
  Check ((Read-Spt $a).revision -eq $s.revision+1) 'Normal withdrawal broken by lock checks'
  'PASS: locked full/split/merge deposits, merge destination, nested locked contents, snapshot permissions; normal withdrawal succeeds'
 }
} finally {
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated fixtures restored.'
}
