param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime',[switch]$BalanceOnly)
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
 foreach($currency in @('RUB','EUR','USD')) {
  Configure $true
  $c=Get-Content $configPath -Raw | ConvertFrom-Json -Depth 100
  $c.CommunityWarehouse | Add-Member FoundInRaidOnly $false -Force
  $c.CommunityWarehouse | Add-Member TransferFees @{Enabled=$true;Currency=$currency;Deposit=100;Withdrawal=200} -Force
  $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
  $tpl=@{RUB='5449016a4bdc2d6f028b456f';EUR='569668774bdc2da2298b4568';USD='5696686a4bdc2da3298b456a'}[$currency]
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
  Start-Test
  $s=Read-Spt $a;Check ($s.fees.currency -eq $currency -and $s.fees.deposit -eq 100) 'Fee snapshot wrong'
  Check ($s.fees.balance -eq 1000) 'Initial balance must include nested money only'
  $null=Send 'deposit' $ammo._id $s.revision
  Check ((Balance $tpl) -eq 900) "Full deposit fee wrong: $(Balance $tpl)"
  Check ((Read-Spt $a).fees.balance -eq 900) 'Snapshot balance did not reflect paid fee'
  if($BalanceOnly) {
   $null=Send 'withdraw' $ammo._id (Read-Spt $a).revision
   Check ((Read-Spt $a).fees.balance -eq 700 -and (Balance $tpl) -eq 700) 'Withdrawal balance differs from saved money'
   "PASS: $currency snapshot balances 1000 -> 900 -> 700 match persisted money, including wallet contents"
   Stop-Test;continue
  }

  $state=Get-Content $statePath -Raw | ConvertFrom-Json -Depth 100
  $first=$state.History[-1]
  Check ($first.Audit.Fee.Amount -eq 100 -and $first.Audit.Fee.Currency -eq $currency) 'Audit fee missing'
  Check ($first.ProfileChanges.Count -eq 3) 'Fee not in same journal as transfer'
  $op=@{Action='HeborianWarehouse';direction='deposit';itemId=$ammo._id;requestId=$first.RequestId;revision=(Read-Spt $a).revision}
  $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Compress)
  Check ((Balance $tpl) -eq 900 -and (Read-Spt $a).revision -eq 1) 'Replay charged twice'
  $null=Send 'withdraw' $ammo._id (Read-Spt $a).revision 5
  Check ((Balance $tpl) -eq 700) 'Partial withdrawal fee wrong'
  $part=(Read-Spt $a).personal | Where-Object { $_.template -eq $ammo._tpl -and $_.id -ne $target._id }
  $null=Merge $a $part.id $true $ammo._id $false (Read-Spt $a).revision 5
  Check ((Balance $tpl) -eq 600) 'Merge deposit fee wrong'
  $null=Merge $a $ammo._id $false $target._id $true (Read-Spt $a).revision 5
  Check ((Balance $tpl) -eq 400) 'Merge withdrawal fee wrong'
  $s=Read-Spt $a
  $op=@{Action='HeborianWarehouse';direction='move';itemId=$ammo._id;requestId=[guid]::NewGuid().ToString('N');revision=$s.revision;x=10;y=5;amount=1}
  $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Compress)
  Check ((Balance $tpl) -eq 400 -and (Read-Spt $a).revision -eq $s.revision+1) 'Internal split charged or failed'
  $s=Read-Spt $a
  $null=Send 'deposit' $wallet._id $s.revision
  Check ((Balance $tpl) -eq 400 -and (Read-Spt $a).revision -eq $s.revision) 'Paid out of transferred wallet'
  $null=Send 'withdraw' $ammo._id (Read-Spt $a).revision
  Check ((Balance $tpl) -eq 200) 'Full withdrawal fee wrong'
  $s=Read-Spt $a
  $op=@{Action='HeborianWarehouse';direction='deposit';itemId=$target._id;requestId=[guid]::NewGuid().ToString('N');revision=$s.revision;x=16;y=0}
  $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Compress)
  Check ((Balance $tpl) -eq 200 -and (Read-Spt $a).revision -eq $s.revision) 'Invalid drop charged'
  $null=Send 'deposit' $target._id $s.revision 1
  Check ((Balance $tpl) -eq 100) 'Partial deposit fee wrong'
  $shared=(Read-Spt $a).items | Where-Object count -eq 1 | Select-Object -First 1
  $s=Read-Spt $a
  $null=Send 'withdraw' $shared.id $s.revision
  Check ((Balance $tpl) -eq 100 -and (Read-Spt $a).revision -eq $s.revision) 'Insufficient funds mutated inventory'
  "PASS: $currency full/split/merge fees, nested funds, excluded wallet, replay, free arrangement, failed drops, insufficient funds and audit"
  Stop-Test
  if($currency -eq 'RUB') {
   $c.CommunityWarehouse.TransferFees.Enabled=$false
   $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
   foreach($saved in @($false,$true)) {
    $p=$fixture | ConvertFrom-Json -Depth 100
    if($saved){foreach($change in $first.ProfileChanges){$p.characters.pmc.Inventory.items=@($p.characters.pmc.Inventory.items | Where-Object _id -ne $change.Id);if($change.After){$p.characters.pmc.Inventory.items+=@($change.After | ConvertFrom-Json -Depth 100)}}}
    $p | ConvertTo-Json -Depth 100 | Set-Content "$TestRoot/user/profiles/$a.json"
    @{Schema=1;Revision=0;Items=@();History=@();Pending=$first} | ConvertTo-Json -Depth 100 | Set-Content $statePath
    Start-Test
    Check ((Balance $tpl) -eq 900 -and (Read-Spt $a).revision -eq 1) 'Recovery fee not exactly once'
    Stop-Test
    Start-Test
    Check ((Balance $tpl) -eq 900 -and (Read-Spt $a).revision -eq 1) 'Second restart charged again'
    Stop-Test
   }
   'PASS: fee+transfer journal recovery before/after save and repeated restart'
  }
  # Disabling fees and zero-priced directions preserve old behaviour.
  $c.CommunityWarehouse.TransferFees.Enabled=$false
  $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
  Start-Test
  $s=Read-Spt $a;$id=$target._id;$balance=Balance $tpl
  $null=Send 'deposit' $id $s.revision
  Check ((Balance $tpl) -eq $balance -and (Read-Spt $a).revision -eq $s.revision+1) 'Disabled fees not free'
  Stop-Test
  $c.CommunityWarehouse.TransferFees.Enabled=$true
  $c.CommunityWarehouse.TransferFees.Deposit=0;$c.CommunityWarehouse.TransferFees.Withdrawal=0
  $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
  Start-Test
  $s=Read-Spt $a
  $null=Send 'withdraw' $id $s.revision
  Check ((Balance $tpl) -eq $balance -and (Read-Spt $a).revision -eq $s.revision+1) 'Zero price not free'
  Stop-Test
  $c.CommunityWarehouse.TransferFees.Deposit=100000
  $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
  Start-Test
  $s=Read-Spt $a
  $null=Send 'deposit' $id $s.revision
  Check ((Balance $tpl) -eq $balance -and (Read-Spt $a).revision -eq $s.revision) 'Expensive fee rejection changed state'
  Stop-Test
  if($currency -eq 'RUB') {
   $p=Get-Content "$TestRoot/user/profiles/$a.json" -Raw | ConvertFrom-Json -Depth 100
   ($p.characters.pmc.Inventory.items | Where-Object _id -eq $cash._id).upd.StackObjectsCount=100100
   $p | ConvertTo-Json -Depth 100 | Set-Content "$TestRoot/user/profiles/$a.json"
   Start-Test
   $s=Read-Spt $a;$null=Send 'deposit' $id $s.revision
   Check ((Balance $tpl) -eq 100 -and (Read-Spt $a).revision -eq $s.revision+1) '100000 RUB fee failed'
   Stop-Test
  }
  "PASS: $currency disabled, zero and 100000 price settings"

 }
}finally{
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated fixtures restored.'
}
