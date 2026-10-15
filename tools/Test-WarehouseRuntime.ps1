param([string]$TestRoot, [string]$ProfileA, [string]$ProfileB, [switch]$RecoveryOnly)
$ErrorActionPreference='Stop'
$TestRoot=(Resolve-Path -LiteralPath $TestRoot).Path
if($TestRoot -ne 'H:\SPT New\Heborian\test-results\warehouse-runtime'){throw 'Unexpected test directory'}
if(!$RecoveryOnly -and (!$ProfileA -or !$ProfileB)){throw 'Pass two isolated test profile IDs.'}
$proc=Start-Process -FilePath "$TestRoot/SPT.Server.exe" -WorkingDirectory $TestRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TestRoot/startup.out.log" -RedirectStandardError "$TestRoot/startup.err.log"
function Invoke-Spt([string]$Id,[string]$Path,[string]$Body='') {
 $args=@{Uri="https://127.0.0.1:6975$Path";SkipCertificateCheck=$true;Headers=@{Cookie="PHPSESSID=$Id";requestcompressed='0'};TimeoutSec=15;OutFile="$TestRoot/response.bin"}
 if($Body){$args.Method='Post';$args.Body=$Body;$args.ContentType='application/json'}
 Invoke-WebRequest @args | Out-Null
 $bytes=[IO.File]::ReadAllBytes("$TestRoot/response.bin")
 if($bytes[0] -eq 120){$source=[IO.MemoryStream]::new($bytes);$z=[IO.Compression.ZLibStream]::new($source,[IO.Compression.CompressionMode]::Decompress);$reader=[IO.StreamReader]::new($z);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose();$source.Dispose()}}else{$text=[Text.Encoding]::UTF8.GetString($bytes)}
 $text | ConvertFrom-Json -Depth 100
}
function Read-Warehouse([string]$Id){ Invoke-Spt $Id '/heborian/warehouse' }
function Move-ItemTest([string]$Id,[string]$Direction,[string]$Item,[long]$Revision,[string]$RequestId,[Nullable[int]]$X=$null,[Nullable[int]]$Y=$null,[Nullable[bool]]$Rotated=$null,[Nullable[int]]$Amount=$null){
 $operation=@{Action='HeborianWarehouse';requestId=$RequestId;direction=$Direction;itemId=$Item;revision=$Revision}
 if($null -ne $X){$operation.x=[int]$X}; if($null -ne $Y){$operation.y=[int]$Y}
 if($null -ne $Rotated){$operation.rotated=[bool]$Rotated}
 if($null -ne $Amount){$operation.amount=[int]$Amount}
 $body=@{data=@($operation);tm=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()} | ConvertTo-Json -Depth 6 -Compress
 Invoke-Spt $Id '/client/game/profile/items/moving' $body
}
try {
 for($i=0;$i -lt 25;$i++){ Start-Sleep -Seconds 1; if($proc.HasExited){throw 'Test server exited'}; if((Get-Content "$TestRoot/startup.out.log" -Raw) -match 'https://127.0.0.1:6975'){break} }
 $a=$ProfileA; $b=$ProfileB
 if($RecoveryOnly){
   $expected=Get-Content "$TestRoot/recovery-expected.json" -Raw | ConvertFrom-Json
   $check=Read-Warehouse $expected.Profile
   if($check.error){throw $check.error}
   $inShared=@($check.items | Where-Object id -EQ $expected.Item).Count
   $inPersonal=@($check.personal | Where-Object id -EQ $expected.Item).Count
   if($inShared -ne $expected.Shared -or $inPersonal -ne $expected.Personal){throw 'Recovered ownership differs from expected'}
   $state=Get-Content "$TestRoot/user/mods/Heborian/data/warehouse.json" -Raw | ConvertFrom-Json -Depth 100
   if($state.Pending){throw 'Recovery not committed'}
   'PASS: real server restart recovered journal; exact owner count verified'
   return
 }
 # Clear remnants from earlier test runs into the original depositor's copied profile.
 $cleanup=Read-Warehouse $a
 foreach($entry in @($cleanup.items)) {
   $null=Move-ItemTest $a withdraw $entry.id $cleanup.revision ([guid]::NewGuid().ToString('N'))
   $cleanup=Read-Warehouse $a
   if($cleanup.items | Where-Object id -EQ $entry.id){throw 'Cannot clear previous test item'}
 }
 $first=Read-Warehouse $a
 $first | ConvertTo-Json -Depth 10 | Set-Content "$TestRoot/read-result.json"
 if($first.error){throw $first.error}
 if($first.personal.Count -eq 0){throw 'No test candidates'}
 $first | ConvertTo-Json -Depth 10 | Set-Content "$TestRoot/read-result.json"
 'PASS: registered route, authenticated profile snapshot; personal candidates: '+$first.personal.Count
 $item=$first.personal[0].id; $rid=[guid]::NewGuid().ToString('N')
 $null=Move-ItemTest $a deposit $item $first.revision ([guid]::NewGuid().ToString('N')) -X ([int]::MaxValue) -Y 0
 $invalid=Read-Warehouse $a
 if($invalid.revision -ne $first.revision -or !($invalid.personal | Where-Object id -EQ $item)){throw 'Invalid drop changed ownership'}
 $null=Move-ItemTest $a deposit $item $first.revision ([guid]::NewGuid().ToString('N')) -X 1
 if((Read-Warehouse $a).revision -ne $first.revision){throw 'Partial coordinates accepted'}
 $candidate=$first.personal[0]; $target=$null
 for($y=7;$y -ge 0 -and !$target;$y--){for($x=7;$x -ge 0 -and !$target;$x--){
   if($x+$candidate.width -gt 8 -or $y+$candidate.height -gt 8){continue}
   $overlap=@($first.items | Where-Object { $x -lt $_.x+$_.width -and $x+$candidate.width -gt $_.x -and $y -lt $_.y+$_.height -and $y+$candidate.height -gt $_.y })
   if(!$overlap.Count){$target=@{x=$x;y=$y}}
 }}
 if(!$target){throw 'No target for positional test'}
 $deposit=Move-ItemTest $a deposit $item $first.revision $rid -X $target.x -Y $target.y
 $deposit | ConvertTo-Json -Depth 20 | Set-Content "$TestRoot/deposit-result.json"
 $after=Read-Warehouse $a
 if(!($after.items | Where-Object id -EQ $item) -or ($after.personal | Where-Object id -EQ $item)){throw 'Deposit did not transfer ownership'}
 $sharedItem=$after.items | Where-Object id -EQ $item
 if($sharedItem.x -ne $target.x -or $sharedItem.y -ne $target.y){throw 'Explicit drop location not preserved'}
 if(($sharedItem.upd | ConvertTo-Json -Depth 30 -Compress) -ne ($candidate.upd | ConvertTo-Json -Depth 30 -Compress)){throw 'Native display metadata changed'}
 $other=$after.personal[0]
 $null=Move-ItemTest $a deposit $other.id $after.revision ([guid]::NewGuid().ToString('N')) -X $target.x -Y $target.y
 if((Read-Warehouse $a).revision -ne $after.revision){throw 'Occupied drop changed warehouse'}
 'PASS: native item metadata preserved; explicit position accepted; invalid/partial/occupied targets rejected'
 'PASS: deposit removed personal item and added shared item'
 # Reposition by another member, leaving both personal inventories untouched.
 $personalBeforeA=($after.personal | ConvertTo-Json -Depth 40 -Compress)
 $beforeB=Read-Warehouse $b
 $personalBeforeB=($beforeB.personal | ConvertTo-Json -Depth 40 -Compress)
 $moveRid=[guid]::NewGuid().ToString('N')
 $null=Move-ItemTest $b move $item $after.revision $moveRid -X 0 -Y 0
 $relocated=Read-Warehouse $a
 $relocatedItem=$relocated.items | Where-Object id -EQ $item
 if($relocated.revision -ne $after.revision+1 -or $relocatedItem.x -ne 0 -or $relocatedItem.y -ne 0){throw 'Shared relocation failed'}
 if(($relocated.personal | ConvertTo-Json -Depth 40 -Compress) -ne $personalBeforeA -or ((Read-Warehouse $b).personal | ConvertTo-Json -Depth 40 -Compress) -ne $personalBeforeB){throw 'Relocation changed a personal inventory'}
 if(($relocatedItem.upd | ConvertTo-Json -Depth 30 -Compress) -ne ($sharedItem.upd | ConvertTo-Json -Depth 30 -Compress)){throw 'Relocation changed item state'}
 $null=Move-ItemTest $a move $item $after.revision ([guid]::NewGuid().ToString('N')) -X 1 -Y 0
 $null=Move-ItemTest $a move $item $relocated.revision $moveRid -X 1 -Y 0
 $null=Move-ItemTest $a move $item $relocated.revision ([guid]::NewGuid().ToString('N')) -X ([int]::MaxValue) -Y 0
 $null=Move-ItemTest $a move $item $relocated.revision ([guid]::NewGuid().ToString('N'))
 if((Read-Warehouse $a).revision -ne $relocated.revision){throw 'Stale/replay/invalid relocation changed state'}
 'PASS: another member relocated shared item; both personal inventories and item state unchanged; stale/replay/invalid moves rejected'
 $null=Move-ItemTest $b move $item $relocated.revision ([guid]::NewGuid().ToString('N')) -X 0 -Y 0 -Rotated $true
 $turned=Read-Warehouse $a
 $turnedItem=$turned.items | Where-Object id -EQ $item
 $expectedRotation=if($sharedItem.width -eq $sharedItem.height){[bool]$sharedItem.rotated}else{$true}
 if($turnedItem.rotated -ne $expectedRotation -or $turnedItem.width -ne $sharedItem.height -or $turnedItem.height -ne $sharedItem.width){throw 'Rotated footprint incorrect'}
 if(($turnedItem.upd | ConvertTo-Json -Depth 30 -Compress) -ne ($sharedItem.upd | ConvertTo-Json -Depth 30 -Compress)){throw 'Rotation changed item state'}
 if(((Read-Warehouse $b).personal | ConvertTo-Json -Depth 40 -Compress) -ne $personalBeforeB){throw 'Rotation changed personal inventory'}
 'PASS: server persisted rotation and exposed swapped footprint to another member, item state unchanged'
 $second=Read-Warehouse $b
 if(!($second.items | Where-Object id -EQ $item)){throw 'Second player did not see shared item'}
 $withdraw=Move-ItemTest $b withdraw $item $second.revision ([guid]::NewGuid().ToString('N'))
 $withdraw | ConvertTo-Json -Depth 20 | Set-Content "$TestRoot/withdraw-result.json"
 $taken=Read-Warehouse $b
 if(($taken.items | Where-Object id -EQ $item) -or !($taken.personal | Where-Object id -EQ $item)){throw 'Withdrawal did not transfer ownership'}
 'PASS: second profile withdrew exact item; shared item gone'
 $stale=Move-ItemTest $a withdraw $item $second.revision ([guid]::NewGuid().ToString('N'))
 $stale | ConvertTo-Json -Depth 20 | Set-Content "$TestRoot/stale-result.json"
 if((Read-Warehouse $a).personal | Where-Object id -EQ $item){throw 'Duplicate item created'}
 'PASS: competing stale withdrawal cannot duplicate the item'
 # Move and withdrawal compete for the same item and revision.
 $raceSource=Read-Warehouse $a
 $raceItem=$raceSource.personal[0].id
 $raceRid=[guid]::NewGuid().ToString('N')
 $null=Move-ItemTest $a deposit $raceItem $raceSource.revision $raceRid
 $raceState=Read-Warehouse $a
 $handler=[Net.Http.HttpClientHandler]::new()
 $handler.ServerCertificateCustomValidationCallback=[Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
 $client=[Net.Http.HttpClient]::new($handler)
 $client.Timeout=[TimeSpan]::FromSeconds(15)
 $messages=@();$tasks=@()
 try {
   foreach($sid in @($a,$b)) {
     $r=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,'https://127.0.0.1:6975/client/game/profile/items/moving')
     $r.Headers.Add('Cookie',"PHPSESSID=$sid");$r.Headers.Add('requestcompressed','0')
     $raceOperation=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='withdraw';itemId=$raceItem;revision=$raceState.revision}
     if($sid -eq $b){$raceOperation.direction='move'; $raceEntry=$raceState.items | Where-Object id -EQ $raceItem; $raceOperation.x=$raceEntry.x; $raceOperation.y=$raceEntry.y}
     $body=@{data=@($raceOperation)} | ConvertTo-Json -Depth 6 -Compress
     $r.Content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
     $messages+=$r; $tasks+=$client.SendAsync($r)
   }
   foreach($task in $tasks){$result=$task.GetAwaiter().GetResult();$null=$result.EnsureSuccessStatusCode();$result.Dispose()}
 } finally {foreach($r in $messages){$r.Dispose()};$client.Dispose();$handler.Dispose()}
 $ra=Read-Warehouse $a; $rb=Read-Warehouse $b
 $owners=@($ra.personal | Where-Object id -EQ $raceItem).Count + @($rb.personal | Where-Object id -EQ $raceItem).Count
 $sharedOwners=@($ra.items | Where-Object id -EQ $raceItem).Count
 if($owners+$sharedOwners -ne 1 -or $ra.revision -ne $raceState.revision+1){throw 'Concurrent move/withdraw invariant failed'}
 if($sharedOwners){$null=Move-ItemTest $a withdraw $raceItem $ra.revision ([guid]::NewGuid().ToString('N')); $ra=Read-Warehouse $a}
 'PASS: concurrent move/withdraw -> one committed operation and exactly one owner'
 # Reuse a committed request ID with a fresh revision; service must stay available.
 $null=Move-ItemTest $a deposit $ra.personal[0].id $ra.revision $raceRid
 $repeat=Read-Warehouse $a
 if($repeat.error -or $repeat.revision -ne $ra.revision){throw 'Replay changed state or stopped service'}
 'PASS: replay with updated revision rejected without disabling warehouse'
 $rotationCandidate=$repeat.personal | Where-Object { $_.width -ne $_.height -and $_.width -le 4 -and $_.height -le 4 } | Select-Object -First 1
 if(!$rotationCandidate){throw 'No rectangular rotation test item'}
 $null=Move-ItemTest $a deposit $rotationCandidate.id $repeat.revision ([guid]::NewGuid().ToString('N')) -X 0 -Y 0 -Rotated $true
 $rotationState=Read-Warehouse $b
 $rotationItem=$rotationState.items | Where-Object id -EQ $rotationCandidate.id
 if(!$rotationItem.rotated -or $rotationItem.width -ne $rotationCandidate.height -or $rotationItem.height -ne $rotationCandidate.width){throw 'Rotated deposit failed'}
 $null=Move-ItemTest $a withdraw $rotationCandidate.id $rotationState.revision ([guid]::NewGuid().ToString('N'))
 if(!((Read-Warehouse $a).personal | Where-Object id -EQ $rotationCandidate.id)){throw 'Rotated deposit/withdraw roundtrip failed'}
 'PASS: rectangular item deposited rotated, visible to second member and withdrawn successfully'
 $splitStart=Read-Warehouse $a
 $stack=$splitStart.personal | Where-Object {$_.count -ge 20 -and $_.width -eq 1 -and $_.height -eq 1} | Select-Object -First 1
 if(!$stack){throw 'Need a test ammunition stack of at least 20'}
 $splitRequest=[guid]::NewGuid().ToString('N')
 $delta=Move-ItemTest $a deposit $stack.id $splitStart.revision $splitRequest -Amount 10
 $splitDeposited=Read-Warehouse $a
 $remaining=$splitDeposited.personal | Where-Object id -EQ $stack.id
 $part=$splitDeposited.items | Where-Object {$_.template -eq $stack.template -and $_.count -eq 10} | Select-Object -First 1
 if(!$part -or $part.id -eq $stack.id -or $remaining.count -ne $stack.count-10){throw 'Partial deposit quantity/ID invariant failed'}
 $delta | ConvertTo-Json -Depth 40 | Set-Content "$TestRoot/partial-deposit-result.json"
 if(!($delta.data.profileChanges.$a.items.change | Where-Object _id -EQ $stack.id)){throw 'Partial deposit missing native changed-item delta'}
 foreach($bad in @(0,-1,11,[int]::MaxValue)){
   $null=Move-ItemTest $b withdraw $part.id $splitDeposited.revision ([guid]::NewGuid().ToString('N')) -Amount $bad
 }
 if((Read-Warehouse $b).revision -ne $splitDeposited.revision){throw 'Invalid amount changed state'}
 $null=Move-ItemTest $b withdraw $part.id $splitDeposited.revision ([guid]::NewGuid().ToString('N')) -Amount 4
 $splitTaken=Read-Warehouse $b
 if(($splitTaken.items | Where-Object id -EQ $part.id).count -ne 6){throw 'Partial withdrawal remainder incorrect'}
 $received=$splitTaken.personal | Where-Object {$_.template -eq $stack.template -and $_.count -eq 4} | Select-Object -First 1
 if(!$received -or $received.id -eq $part.id){throw 'Partial withdrawal new ID/count incorrect'}
 $null=Move-ItemTest $a move $part.id $splitTaken.revision ([guid]::NewGuid().ToString('N')) -X 7 -Y 7 -Amount 2
 $splitLocal=Read-Warehouse $a
 if(($splitLocal.items | Where-Object id -EQ $part.id).count -ne 4 -or !($splitLocal.items | Where-Object {$_.template -eq $stack.template -and $_.count -eq 2 -and $_.x -eq 7 -and $_.y -eq 7})){throw 'Local shared split failed'}
 $allA=Read-Warehouse $a; $allB=Read-Warehouse $b
 $total=(@($allA.personal)+@($allB.personal)+@($allA.items) | Where-Object template -EQ $stack.template | Measure-Object count -Sum).Sum
 $beforeTotal=(@($splitStart.personal)+@($splitStart.items)+@($beforeB.personal) | Where-Object template -EQ $stack.template | Measure-Object count -Sum).Sum
 if($total -ne $beforeTotal){throw 'Split operations created or lost quantity'}
 $null=Move-ItemTest $a deposit $stack.id $allA.revision $splitRequest -Amount 1
 if((Read-Warehouse $a).revision -ne $allA.revision){throw 'Partial request replay accepted'}
 'PASS: partial deposit/withdraw/shared split, profile delta, unique IDs, invalid amounts/replay and total quantity conservation'
 $partialRace=Read-Warehouse $a
 $sourcePart=$partialRace.items | Where-Object id -EQ $part.id
 $null=Move-ItemTest $a move $part.id $partialRace.revision ([guid]::NewGuid().ToString('N')) -X $sourcePart.x -Y $sourcePart.y -Amount 1
 if((Read-Warehouse $a).revision -ne $partialRace.revision){throw 'Split overlapped its remaining source stack'}
 $handler=[Net.Http.HttpClientHandler]::new()
 $handler.ServerCertificateCustomValidationCallback=[Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
 $client=[Net.Http.HttpClient]::new($handler);$messages=@();$tasks=@()
 try {
  foreach($sid in @($a,$b)){
   $message=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,'https://127.0.0.1:6975/client/game/profile/items/moving')
   $message.Headers.Add('Cookie',"PHPSESSID=$sid");$message.Headers.Add('requestcompressed','0')
   $body=@{data=@(@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='withdraw';itemId=$part.id;revision=$partialRace.revision;amount=3})} | ConvertTo-Json -Depth 6 -Compress
   $message.Content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
   $messages+=$message;$tasks+=$client.SendAsync($message)
  }
  foreach($task in $tasks){$response=$task.GetAwaiter().GetResult();$null=$response.EnsureSuccessStatusCode();$response.Dispose()}
 } finally {foreach($message in $messages){$message.Dispose()};$client.Dispose();$handler.Dispose()}
 $finalA=Read-Warehouse $a; $finalB=Read-Warehouse $b
 if($finalA.revision -ne $partialRace.revision+1 -or ($finalA.items | Where-Object id -EQ $part.id).count -ne 1){throw 'Concurrent partial withdrawals overspent the stack'}
 $finalTotal=(@($finalA.personal)+@($finalB.personal)+@($finalA.items) | Where-Object template -EQ $stack.template | Measure-Object count -Sum).Sum
 if($finalTotal -ne $beforeTotal){throw 'Concurrent partial withdrawal quantity mismatch'}
 'PASS: two concurrent partial withdrawals commit once; no overdraw, overlap, duplication or loss'
} finally {
 if(!$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 Get-Content "$TestRoot/startup.out.log" -Tail 18
 Get-Content "$TestRoot/startup.err.log" -Tail 6
 'Test server stopped.'
}

