param([string]$TestRoot='H:\SPT New\Heborian\test-results\warehouse-runtime',[string]$ModVersion,[switch]$PaidFees)
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
 $c=Get-Content $configPath -Raw | ConvertFrom-Json -Depth 100
 $c.CommunityWarehouse | Add-Member TransferFees @{Enabled=[bool]$PaidFees;Currency='RUB';Deposit=10;Withdrawal=10} -Force
 $c | ConvertTo-Json -Depth 100 | Set-Content $configPath
 foreach($id in @($a,$b)) {
  $p=$originalProfiles[$id] | ConvertFrom-Json -Depth 100
  $inv=$p.characters.pmc.Inventory
  $removed=[Collections.Generic.HashSet[string]]::new()
  foreach($root in @($inv.items | Where-Object {$_.parentId -eq $inv.stash})) { $null=$removed.Add($root._id) }
  do { $more=$false; foreach($item in $inv.items){if($removed.Contains($item.parentId) -and $removed.Add($item._id)){$more=$true}} } while($more)
  $inv.items=@($inv.items | Where-Object {!$removed.Contains($_._id)})
  if($PaidFees) {
   $cash=Item $(if($id -eq $a){'aaaaaaaaaaaaaaaaaaaaaaa1'}else{'bbbbbbbbbbbbbbbbbbbbbbb1'}) 100000 $inv.stash 9
   $cash._tpl='5449016a4bdc2d6f028b456f'
   $inv.items+=@($cash)
  }
  $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$id.json"
 }
 $entries=@()
 for($i=0;$i -lt 48;$i++) {
  $one=Item ('cccc{0:x20}' -f $i) 10 $inv.stash ($i%16)
  $one.location.y=[int][math]::Floor($i/16)
  $entries+=@{Id=$one._id;Template=$one._tpl;Name='Ammo';Json=($one|ConvertTo-Json -Depth 10 -Compress);X=$one.location.x;Y=$one.location.y;Width=1;Height=1;Count=10}
 }
 @{Schema=1;Revision=0;Items=$entries;History=@();Pending=$null} | ConvertTo-Json -Depth 100 | Set-Content $statePath
 Start-Test
 $handler=[Net.Http.HttpClientHandler]::new()
 # Each request supplies a different explicit session; never merge Set-Cookie into later requests.
 $handler.UseCookies=$false
 $handler.ServerCertificateCustomValidationCallback=[Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
 $client=[Net.Http.HttpClient]::new($handler)
 try {
  foreach($entry in $entries) {
   $s=Read-Spt $a; Check (!$s.error) $s.error
   $tasks=@();$messages=@()
   try {
    # The game's inventory queue permits one outstanding mutation per profile.
    # Parallel requests from the same profile can race in SPT before our handler.
    for($j=0;$j -lt 2;$j++) {
     $id=if($j%2){$a}else{$b}
     $op=@{Action='HeborianWarehouse';direction='withdraw';itemId=$entry.Id;revision=$s.revision;requestId=[guid]::NewGuid().ToString('N')}
     if($entry.X%2){$op.amount=3}
     $m=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,'https://127.0.0.1:6975/client/game/profile/items/moving')
     $m.Headers.Add('Cookie',"PHPSESSID=$id");$m.Headers.Add('requestcompressed','0')
     $m.Content=[Net.Http.StringContent]::new((@{data=@($op)}|ConvertTo-Json -Depth 8 -Compress),[Text.Encoding]::UTF8,'application/json')
     $messages+=$m;$tasks+=$client.SendAsync($m)
    }
    $successes=0;$failures=0
    $responseIndex=0
    foreach($task in $tasks){
     $response=$task.GetAwaiter().GetResult()
     try {
      $null=$response.EnsureSuccessStatusCode()
      $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
      if($bytes[0] -eq 120){
       $ms=[IO.MemoryStream]::new($bytes);$z=[IO.Compression.ZLibStream]::new($ms,[IO.Compression.CompressionMode]::Decompress);$reader=[IO.StreamReader]::new($z)
       try{$text=$reader.ReadToEnd()}finally{$reader.Dispose();$ms.Dispose()}
      }else{$text=[Text.Encoding]::UTF8.GetString($bytes)}
      $reply=$text | ConvertFrom-Json -Depth 100

      Check ($null -ne $reply.data -and $reply.err -eq 0) 'Missing native inventory response'
      $expectedProfile=if($responseIndex%2){$a}else{$b};$responseIndex++
      Check (($reply.data.profileChanges.PSObject.Properties.Name -join ',') -eq $expectedProfile) 'Response belongs to another session'
      if(@($reply.data.warnings).Count -gt 0){$failures++}
      else {
       $successes++
       $profiles=@($reply.data.profileChanges.PSObject.Properties)
       Check ($profiles.Count -eq 1) 'Inventory response leaked another profile'
       Check (@($profiles[0].Value.items.new).Count -eq 1) 'Successful withdrawal missing client item delta'
      }
     }finally{$response.Dispose()}
    }
    Check ($successes -eq 1 -and $failures -eq 1) "Concurrent responses: revision=$($s.revision) successes=$successes failures=$failures" 
   }finally{foreach($m in $messages){$m.Dispose()}}
   $final=Read-Spt $a;$other=Read-Spt $b
   Check ($final.revision -eq $s.revision+1) 'Concurrent withdrawals did not commit exactly once'
   $all=@($final.items)+@($final.personal)+@($other.personal)
   Check ((($all | Where-Object template -eq '56dff061d2720bb5668b4567')|Measure-Object count -Sum).Sum -eq 480) 'Concurrent withdrawals lost or duplicated ammunition'
   if($PaidFees){Check (($final.fees.balance+$other.fees.balance) -eq (200000-10*$final.revision)) 'Concurrent fee charged twice or omitted'}
   $ids=@($all | ForEach-Object {$_.id})
   Check (($ids | Select-Object -Unique).Count -eq $ids.Count) 'One item ID has multiple owners'
   if($entry.X%2){Check (($final.items | Where-Object id -eq $entry.Id).count -eq 7) 'Partial concurrent withdrawal applied more than once'}
   else{Check (@($all | Where-Object id -eq $entry.Id).Count -eq 1) 'Full concurrent withdrawal duplicated ownership'}
  }
 }finally{$client.Dispose();$handler.Dispose()}
 if($PaidFees){'PASS: paid concurrency charges exactly one 10 RUB fee per winning transfer, no charge for rejected requests'}
 'PASS: 96 API requests in 48 batches across two profiles (one outstanding request per profile); each same-revision batch commits exactly once; full/partial withdrawals conserve all 480 rounds and unique item ownership; one successful client delta and one rejected response per batch'
}finally{
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
