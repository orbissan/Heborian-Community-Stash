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
try {
 foreach($id in @($a,$b)){
  $p=$originalProfiles[$id] | ConvertFrom-Json -Depth 100
  $inv=$p.characters.pmc.Inventory
  $inv.items=@($inv.items | Where-Object { $_.slotId -ne 'hideout' })
  if($id -eq $a){$inv.items+=@( (Item 'aaaaaaaaaaaaaaaaaaaaaaa1' 20 $inv.stash 0),(Item 'aaaaaaaaaaaaaaaaaaaaaaa2' 10 $inv.stash 1),(Item 'aaaaaaaaaaaaaaaaaaaaaaa3' 20 $inv.stash 2),(Item 'aaaaaaaaaaaaaaaaaaaaaaa4' 10 $inv.stash 3) )}
  else{$inv.items+=@(Item 'bbbbbbbbbbbbbbbbbbbbbbb1' 20 $inv.stash 0)}
  $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$id.json"
 }
 $entries=@();$x=0
 foreach($seed in @(@('ccccccccccccccccccccccc1',20,$false),@('ccccccccccccccccccccccc2',55,$false),@('ccccccccccccccccccccccc3',10,$true))){
  $item=Item $seed[0] $seed[1] 'warehouse' $x $seed[2]
  $entries+=@{Id=$seed[0];Template=$item._tpl;Name='Test ammo';Json=($item | ConvertTo-Json -Depth 10 -Compress);Width=1;Height=1;X=$x;Y=0;Count=$seed[1];Rotated=$false};$x++
 }
 @{Schema=1;Revision=0;Items=$entries;History=@();Pending=$null} | ConvertTo-Json -Depth 100 -Compress | Set-Content $statePath
 $version=& "$PSScriptRoot/Get-Version.ps1"
 Copy-Item "H:\SPT New\Heborian\artifacts\$version\SPT_Runtime\user\mods\Heborian\Heborian.dll" "$TestRoot/user/mods/Heborian/Heborian.dll" -Force
 $proc=Start-Process "$TestRoot/SPT.Server.exe" -WorkingDirectory $TestRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TestRoot/merge.out.log" -RedirectStandardError "$TestRoot/merge.err.log"
 for($i=0;$i -lt 30;$i++){Start-Sleep -Seconds 1;if($proc.HasExited){throw 'Test server exited'};if((Get-Content "$TestRoot/merge.out.log" -Raw) -match 'https://127.0.0.1:6975'){break}}
 $s=Read-Spt $a; Check (!$s.error) $s.error
 Check ($s.width -eq 16 -and $s.height -eq 32) 'Expanded dimensions not exposed'
 foreach($x in @(15,16)){
  $oldRevision=$s.revision
  $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='move';itemId='ccccccccccccccccccccccc1';revision=$s.revision;x=$x;y=31}
  $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Depth 8 -Compress)
  $s=Read-Spt $a
  if($x -eq 15){Check (($s.items | Where-Object id -EQ 'ccccccccccccccccccccccc1').x -eq 15 -and $s.revision -eq $oldRevision+1) 'Expanded last corner unreachable'}
  else{Check ($s.revision -eq $oldRevision) 'Out-of-bounds expanded placement accepted'}
 }
 'PASS: 16x32 snapshot, last-corner move and outside-boundary rejection'
 Check (($s.personal | Where-Object id -EQ 'aaaaaaaaaaaaaaaaaaaaaaa1').maxStack -eq 60) 'Unexpected template stack maximum'
 $null=Merge $a 'ccccccccccccccccccccccc1' $false 'ccccccccccccccccccccccc2' $false $s.revision
 $s=Read-Spt $a;Check (($s.items | Where-Object id -EQ 'ccccccccccccccccccccccc1').count -eq 15) 'Capacity remainder wrong'
 Check (($s.items | Where-Object id -EQ 'ccccccccccccccccccccccc2').count -eq 60) 'Target maximum wrong'
 'PASS: shared merge caps target and preserves source remainder'
 $rid=[guid]::NewGuid().ToString('N');$rev=$s.revision
 $result=Merge $a 'aaaaaaaaaaaaaaaaaaaaaaa1' $true 'ccccccccccccccccccccccc1' $false $rev $null $rid
 $s=Read-Spt $a;Check (($s.items | Where-Object id -EQ 'ccccccccccccccccccccccc1').count -eq 35) 'Deposit merge wrong'
 Check (!($s.personal | Where-Object id -EQ 'aaaaaaaaaaaaaaaaaaaaaaa1')) 'Consumed personal source remains'
 Check (($result.data.profileChanges.$a.items.del | Where-Object _id -EQ 'aaaaaaaaaaaaaaaaaaaaaaa1')) 'Missing personal delete delta'
 $null=Merge $a 'aaaaaaaaaaaaaaaaaaaaaaa2' $true 'ccccccccccccccccccccccc1' $false $rev
 $null=Merge $a 'aaaaaaaaaaaaaaaaaaaaaaa2' $true 'ccccccccccccccccccccccc1' $false $s.revision $null $rid
 Check ((Read-Spt $a).revision -eq $s.revision) 'Stale/replay accepted'
 'PASS: deposit merge removes consumed source; profile delta, stale revision and replay'
 $null=Merge $a 'ccccccccccccccccccccccc1' $false 'aaaaaaaaaaaaaaaaaaaaaaa2' $true $s.revision
 $s=Read-Spt $a;Check (($s.personal | Where-Object id -EQ 'aaaaaaaaaaaaaaaaaaaaaaa2').count -eq 45) 'Withdrawal merge wrong'
 Check (!($s.items | Where-Object id -EQ 'ccccccccccccccccccccccc1')) 'Consumed shared source remains'
 $null=Merge $a 'aaaaaaaaaaaaaaaaaaaaaaa3' $true 'aaaaaaaaaaaaaaaaaaaaaaa4' $true $s.revision
 $s=Read-Spt $a;Check (($s.personal | Where-Object id -EQ 'aaaaaaaaaaaaaaaaaaaaaaa4').count -eq 30) 'Personal merge wrong'
 $null=Merge $a 'ccccccccccccccccccccccc2' $false 'aaaaaaaaaaaaaaaaaaaaaaa2' $true $s.revision 5
 $s=Read-Spt $a;Check (($s.personal | Where-Object id -EQ 'aaaaaaaaaaaaaaaaaaaaaaa2').count -eq 50) 'Requested merge amount wrong'
 'PASS: shared-to-personal, personal-to-personal and selected merge amount'
 foreach($amount in @(0,-1,999)){$null=Merge $a 'ccccccccccccccccccccccc2' $false 'aaaaaaaaaaaaaaaaaaaaaaa2' $true $s.revision $amount}
 $null=Merge $a 'ccccccccccccccccccccccc3' $false 'aaaaaaaaaaaaaaaaaaaaaaa2' $true $s.revision
 $null=Merge $a 'ccccccccccccccccccccccc2' $false 'ccccccccccccccccccccccc2' $false $s.revision
 Check ((Read-Spt $a).revision -eq $s.revision) 'Invalid merge changed revision'
 'PASS: FIR mismatch, self merge and invalid quantities rejected'
 $beforeTotal=(@($s.items)+@($s.personal)+@((Read-Spt $b).personal) | Measure-Object count -Sum).Sum
 $handler=[Net.Http.HttpClientHandler]::new();$handler.ServerCertificateCustomValidationCallback=[Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
 $client=[Net.Http.HttpClient]::new($handler);$tasks=@();$messages=@()
 try {
  foreach($id in @($a,$b)){
   $target=if($id -eq $a){'aaaaaaaaaaaaaaaaaaaaaaa2'}else{'bbbbbbbbbbbbbbbbbbbbbbb1'}
   $m=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,'https://127.0.0.1:6975/client/game/profile/items/moving')
   $m.Headers.Add('Cookie',"PHPSESSID=$id");$m.Headers.Add('requestcompressed','0')
   $body=@{data=@(@{Action='HeborianWarehouse';direction='withdraw';itemId='ccccccccccccccccccccccc2';targetId=$target;targetPersonal=$true;revision=$s.revision;requestId=[guid]::NewGuid().ToString('N')})}|ConvertTo-Json -Depth 8 -Compress
   $m.Content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json');$messages+=$m;$tasks+=$client.SendAsync($m)
  }
  foreach($task in $tasks){$response=$task.GetAwaiter().GetResult();$null=$response.EnsureSuccessStatusCode();$response.Dispose()}
 }finally{foreach($m in $messages){$m.Dispose()};$client.Dispose();$handler.Dispose()}
 $final=Read-Spt $a;$other=Read-Spt $b
 Check ($final.revision -eq $s.revision+1) 'Concurrent merges committed more than once'
 $afterTotal=(@($final.items)+@($final.personal)+@($other.personal)|Measure-Object count -Sum).Sum
 Check ($beforeTotal -eq $afterTotal) 'Merge lost or duplicated quantity'
 'PASS: concurrent merges commit once and conserve all ammunition'
 Copy-Item $statePath "$TestRoot/merge-tested-state.json" -Force
}finally{
 if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
