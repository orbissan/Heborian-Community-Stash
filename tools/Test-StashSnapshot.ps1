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
 $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
 $inv=$p.characters.pmc.Inventory
 # Add a rotated unsupported container, with a nested item, to the isolated profile.
 $case=@{_id='ddddddddddddddddddddddd1';_tpl='5857a8b324597729ab0a0e7d';parentId=$inv.stash;slotId='hideout';location=@{x=6;y=12;r=1;isSearched=$true};upd=@{StackObjectsCount=1}}
 $child=Item 'ddddddddddddddddddddddd2' 12 $case._id 0
 $child.slotId="main"
 $inv.items+=@($case,$child)
 $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
 $version=& "$PSScriptRoot/Get-Version.ps1"
 Copy-Item "H:\SPT New\Heborian\artifacts\$version\SPT_Runtime\user\mods\Heborian\Heborian.dll" "$TestRoot/user/mods/Heborian/Heborian.dll" -Force
 $proc=Start-Process "$TestRoot/SPT.Server.exe" -WorkingDirectory $TestRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TestRoot/merge.out.log" -RedirectStandardError "$TestRoot/merge.err.log"
 for($i=0;$i -lt 30;$i++){Start-Sleep -Seconds 1;if($proc.HasExited){throw 'Test server exited'};if((Get-Content "$TestRoot/merge.out.log" -Raw) -match 'https://127.0.0.1:6975'){break}}
 $s=Read-Spt $a; Check (!$s.error) $s.error
 Check ($s.personalWidth -ge 10 -and $s.personalHeight -ge 20) 'Stash dimensions missing'
 $roots=@($inv.items | Where-Object { $_.parentId -eq $inv.stash -and $_.slotId -eq 'hideout' })
 Check ($s.personal.Count -eq $roots.Count) 'Not all direct stash items exposed'
 foreach($root in $roots){
  $view=$s.personal | Where-Object id -eq $root._id
  Check ($view.x -eq $root.location.x -and $view.y -eq $root.location.y) 'Stash coordinates changed'
  Check ($view.tree[0]._id -eq $root._id) 'Root item tree missing'
 }
 $blocked=$s.personal | Where-Object id -eq $case._id
 Check (!$blocked.transferable -and $blocked.rotated) 'Unsupported rotated container not flagged'
 Check ($blocked.width -eq 2 -and $blocked.height -eq 3) 'Rotated container footprint wrong'
 Check ($blocked.tree.Count -eq 2) 'Nested item tree omitted'
 Check (!($s.personal | Where-Object id -eq $child._id)) 'Nested item duplicated in stash grid'
 Check (@($s.personal | Where-Object transferable).Count -gt 0) 'Transferable items lost'
 $op=@{Action='HeborianWarehouse';requestId=[guid]::NewGuid().ToString('N');direction='deposit';itemId=$case._id;revision=$s.revision}
 $null=Read-Spt $a (@{data=@($op)} | ConvertTo-Json -Depth 8 -Compress)
 $after=Read-Spt $a
 Check ($after.revision -eq $s.revision -and ($after.personal | Where-Object id -eq $case._id)) 'Blocked item deposit accepted'
 'PASS: actual stash dimensions, all roots, original positions, rotation/footprint, nested trees and transfer restrictions'
}finally{
 if($proc -and !$proc.HasExited){$proc.Kill($true);$proc.WaitForExit()}
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
