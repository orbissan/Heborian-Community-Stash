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
try {
 Configure $true
 $config=Get-Content $configPath -Raw | ConvertFrom-Json -Depth 100
 $config.CommunityWarehouse | Add-Member FoundInRaidOnly $false -Force
 $config | ConvertTo-Json -Depth 100 | Set-Content $configPath
 $db=Get-Content 'H:/SPT New/SPT_Runtime/SPT_Data/database/templates/items.json' -Raw | ConvertFrom-Json -AsHashtable -Depth 100
 $templates=@('5783c43d2459774bbe137486','59fb016586f7746d0d4b423a','59fafd4b86f7745ca07e1232','5aafbde786f774389d0cbc0f','5aafbcd986f7745e590fff23','5c093db286f7740a1b2617e3','59fb042886f7746c5005a7b2','5c0e805e86f774683f3dd637','5c127c4486f7745625356c13','59fb023c86f7746d0d4b423c','5e2af55f86f7746d4159f07c','619cbf7d23893217ec30b689','619cbf9e0a7c3a1a2731940a','590c60fc86f77412b13fddcf','5857a8bc2459772bad15db29','544a5caa4bdc2d1a388b4568')
 function New-ItemData($tpl,$parent,$slot,$x,$y,$count=1) {
  @{_id=[guid]::NewGuid().ToString('N').Substring(0,24);_tpl=$tpl;parentId=$parent;slotId=$slot;location=@{x=$x;y=$y;r=0;isSearched=$true};upd=@{StackObjectsCount=$count;SpawnedInSession=$true}}
 }
 function Allowed($tpl,$grid) {
  $ancestry=@($tpl); $current=$tpl
  while($db.ContainsKey($current) -and $db[$current]._parent){$current=$db[$current]._parent;$ancestry+=@($current)}
  foreach($filter in $grid._props.filters) {
   if(@($filter.ExcludedFilter | Where-Object {$_ -in $ancestry}).Count){return $false}
   if($filter.Filter.Count -and !@($filter.Filter | Where-Object {$_ -in $ancestry}).Count){return $false}
  }
  return $true
 }
 $candidates=@('5449016a4bdc2d6f028b456f','56dff061d2720bb5668b4567','544fb25a4bdc2dfb738b4567','57513f07245977207e26a311')
 # Include real 1x1 keys and other restricted-grid candidates from installed templates.
 $candidates+=@($db.Keys | Sort-Object | Where-Object {$db[$_]._type -eq 'Item' -and $db[$_]._props.Width -eq 1 -and $db[$_]._props.Height -eq 1 -and !$db[$_]._props.QuestItem -and !$db[$_]._props.Grids})
 foreach($mode in @('empty','partial','full')) {
  Stop-Test
  $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
  $p.characters.pmc.InsuredItems=@();$inv=$p.characters.pmc.Inventory
  $remove=[Collections.Generic.HashSet[string]]::new()
  foreach($i in $inv.items){if($i.parentId -eq $inv.stash){$null=$remove.Add($i._id)}}
  do{$added=$false;foreach($i in $inv.items){if($remove.Contains($i.parentId) -and $remove.Add($i._id)){$added=$true}}}while($added)
  $inv.items=@($inv.items | Where-Object {!$remove.Contains($_._id)})
  $roots=@();$trees=@{};$y=0
  foreach($tpl in $templates) {
   $root=New-ItemData $tpl $inv.stash 'hideout' 0 $y
   $y+=[int]$db[$tpl]._props.Height
   $tree=@($root)
   if($mode -ne 'empty') {
    foreach($grid in $db[$tpl]._props.Grids) {
     $allowed=@($candidates | Where-Object {Allowed $_ $grid} | Select-Object -First 3)
     Check ($allowed.Count -gt 0) "No valid content fixture for $tpl/$($grid._name)"
     $cells=if($mode -eq 'full'){[int]$grid._props.cellsH * [int]$grid._props.cellsV}else{1}
     for($n=0;$n -lt $cells;$n++) {
      $child=New-ItemData $allowed[$n % $allowed.Count] $root._id $grid._name ($n % [int]$grid._props.cellsH) ([int][Math]::Floor($n/[int]$grid._props.cellsH))
      $tree+=@($child)
     }
    }
    if($tpl -eq '5c0e805e86f774683f3dd637' -and $mode -eq 'partial') {
     # A wallet inside the backpack, with a money stack inside the wallet (three levels).
     $wallet=New-ItemData '5783c43d2459774bbe137486' $root._id 'main' 1 0
     $cash=New-ItemData '5449016a4bdc2d6f028b456f' $wallet._id 'main' 0 0 23456
     $tree+=@($wallet,$cash)
    }
   }
   $roots+=@($root);$trees[$root._id]=$tree;$inv.items+=@($tree)
  }
  $other=$originalProfiles[$b] | ConvertFrom-Json -Depth 100
  # Use the same structural IDs as the clean fixture, without any test container descendants.
  $allTestIds=@($trees.Values | ForEach-Object {$_} | ForEach-Object {$_._id})
  $other.characters.pmc.Inventory=$inv | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
  $other.characters.pmc.Inventory.items=@($other.characters.pmc.Inventory.items | Where-Object {$_._id -notin $allTestIds})
  $other.characters.pmc.InsuredItems=@()
  $other | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$b.json"
  if($mode -eq 'empty') {
   $looseCash=New-ItemData '5449016a4bdc2d6f028b456f' $inv.stash 'hideout' 9 0 20
   $inv.items+=@($looseCash)
  }
  $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
  @{Schema=1;Revision=0;Items=@();History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
  Start-Test
  foreach($root in $roots) {
   $s=Read-Spt $a;Check (!$s.error) $s.error
   $view=$s.personal | Where-Object id -eq $root._id
   Check ($view.transferable) "Not transferable: $($root._tpl)"
   $before=$view.tree
   $r=Send 'deposit' $root._id $s.revision
   Check (@($r.data.profileChanges.$a.items.del).Count -eq 1 -and $r.data.profileChanges.$a.items.del[0]._id -eq $root._id) 'Client delta must delete only the root'
   $persisted=Get-Content "$TestRoot/user/profiles/$a.json" -Raw | ConvertFrom-Json -Depth 100
   Check (@($persisted.characters.pmc.Inventory.items | Where-Object { $_._id -in $before._id }).Count -eq 0) 'Server profile retains a transferred descendant'
   $shared=Read-Spt $a;$stored=$shared.items | Where-Object id -eq $root._id
   Check ($stored.tree.Count -eq $before.Count) 'Incomplete stored tree'
   # A stale/repeated request must not alter either inventory.
   $null=Send 'deposit' $root._id $s.revision
   Check ((Read-Spt $a).revision -eq $shared.revision) 'Stale request mutated state'
   # Invalid placement must preserve the complete container.
   $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='move';itemId=$root._id;revision=$shared.revision;x=16;y=0}
   $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Compress)
   Check ((Read-Spt $a).revision -eq $shared.revision) 'Invalid placement mutated state'
   $r=Send 'withdraw' $root._id $shared.revision
   Check (@($r.data.profileChanges.$a.items.new).Count -eq $before.Count) 'Incomplete creation delta'
   $after=(Read-Spt $a).personal | Where-Object id -eq $root._id
   Check ($after.tree.Count -eq $before.Count) 'Roundtrip lost descendants'
   foreach($item in $before) {
    $matches=@($after.tree | Where-Object _id -eq $item._id)
    Check ($matches.Count -eq 1) 'Missing or duplicated item'
    $actual=$matches[0]
    Check ($actual._tpl -eq $item._tpl) 'Template changed'
    Check (($actual.upd | ConvertTo-Json -Depth 30 -Compress) -eq ($item.upd | ConvertTo-Json -Depth 30 -Compress)) 'Item state changed'
    if($item._id -ne $root._id) {
     Check ($actual.parentId -eq $item.parentId -and $actual.slotId -eq $item.slotId) 'Container relationship changed'
     Check (($actual.location | ConvertTo-Json -Compress) -eq ($item.location | ConvertTo-Json -Compress)) 'Child position changed'
    }
   }
   if($root._tpl -eq '5783c43d2459774bbe137486' -and $mode -eq 'empty') {
    # Reproduce the user's sequence: return the empty wallet, fill it in Character,
    # then deposit/withdraw it again, using SPT's ordinary Move endpoint to fill it.
    $op=@{Action='Move';item=$looseCash._id;to=@{id=$root._id;container='main';location=@{x=0;y=0;r=0}}}
    $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Depth 10 -Compress)
    $filled=(Read-Spt $a).personal | Where-Object id -eq $root._id
    Check ($filled.tree.Count -eq 2) 'Native inventory Move did not fill wallet'
    $r=Send 'deposit' $root._id (Read-Spt $a).revision
    Check (@($r.data.profileChanges.$a.items.del).Count -eq 1) 'Filled wallet emitted child deletion'
    $null=Send 'withdraw' $root._id (Read-Spt $a).revision
    $returned=(Read-Spt $a).personal | Where-Object id -eq $root._id
    Check ($returned.tree.Count -eq 2 -and ($returned.tree | Where-Object _id -eq $looseCash._id).upd.StackObjectsCount -eq 20) 'Refilled wallet lost money'
    'PASS: empty wallet roundtrip -> native inventory Move of 20 roubles -> filled wallet roundtrip'
   }
   if($root._tpl -eq '5c0e805e86f774683f3dd637' -and $mode -eq 'partial') {
    $null=Send 'deposit' $root._id (Read-Spt $a).revision
    $r=Send 'withdraw' $root._id (Read-Spt $b).revision $null $b
    Check (@($r.data.profileChanges.$b.items.new).Count -eq $before.Count) 'Other-player withdrawal delta incomplete'
    $received=(Read-Spt $b).personal | Where-Object id -eq $root._id
    Check ($received.tree.Count -eq $before.Count) 'Other player lost nested contents'
    foreach($item in $before){Check (@($received.tree | Where-Object _id -eq $item._id).Count -eq 1) 'Other player lost/duplicated ID'}
    $null=Send 'deposit' $root._id (Read-Spt $b).revision $null $b
    $null=Send 'withdraw' $root._id (Read-Spt $a).revision
    Check (((Read-Spt $a).personal | Where-Object id -eq $root._id).tree.Count -eq $before.Count) 'Cross-player return failed'
    'PASS: nested backpack/wallet/currency transfer between two profiles and back'
   }
   "PASS: $mode $($db[$root._tpl]._name), $($before.Count) items, exact roundtrip + stale/invalid requests"
  }
 }
}finally{
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
