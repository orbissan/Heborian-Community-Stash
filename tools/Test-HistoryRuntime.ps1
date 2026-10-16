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
$historyPath=Join-Path $TestRoot 'user/mods/Heborian/data/history'
$backup=Join-Path $TestRoot ('user/mods/Heborian/data/history-backup-'+[guid]::NewGuid().ToString('N'))
$archiveResult=Join-Path $TestRoot ('user/mods/Heborian/data/history-test-'+[guid]::NewGuid().ToString('N'))
foreach($target in @($historyPath,$backup,$archiveResult)) {if(![IO.Path]::GetFullPath($target).StartsWith($TestRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture path'}}
$hadHistory=Test-Path -LiteralPath $historyPath
if($hadHistory){Move-Item -LiteralPath $historyPath -Destination $backup}
function Read-History($filter='all',[int]$page=0,$search='',$session=$a) {
 $body=@{page=$page;filter=$filter;search=$search;throughRevision=0}|ConvertTo-Json -Compress
 Invoke-WebRequest -Uri 'https://127.0.0.1:6975/heborian/warehouse/history' -Method Post -Body $body -ContentType 'application/json' -SkipCertificateCheck -Headers @{Cookie="PHPSESSID=$session";requestcompressed='0'} -OutFile "$TestRoot/history-response.bin" | Out-Null
 $bytes=[IO.File]::ReadAllBytes("$TestRoot/history-response.bin")
 if($bytes[0] -eq 120){$ms=[IO.MemoryStream]::new($bytes);$z=[IO.Compression.ZLibStream]::new($ms,[IO.Compression.CompressionMode]::Decompress);$r=[IO.StreamReader]::new($z);try{$text=$r.ReadToEnd()}finally{$r.Dispose();$ms.Dispose()}}else{$text=[Text.Encoding]::UTF8.GetString($bytes)}
 $text|ConvertFrom-Json -Depth 100
}
function Operation($op) {$op.Action='HeborianWarehouse';$op.requestId=[guid]::NewGuid().ToString('N');$null=Read-Spt $a (@{data=@($op)}|ConvertTo-Json -Depth 12 -Compress)}
function HistoryConfig($enabled,$public) {
 $config=Get-Content $configPath -Raw|ConvertFrom-Json -Depth 100
 $config.CommunityWarehouse|Add-Member History @{Enabled=$enabled;Public=$public;RetentionDays=30} -Force
 $config|ConvertTo-Json -Depth 100|Set-Content $configPath
}
try {
 Configure $true;HistoryConfig $true $true
 $p=$originalProfiles[$a]|ConvertFrom-Json -Depth 100;$inv=$p.characters.pmc.Inventory
 $removed=[Collections.Generic.HashSet[string]]::new()
 foreach($root in @($inv.items|Where-Object {$_.parentId -eq $inv.stash})){$null=$removed.Add($root._id)}
 do{$more=$false;foreach($item in $inv.items){if($removed.Contains($item.parentId) -and $removed.Add($item._id)){$more=$true}}}while($more)
 $inv.items=@($inv.items|Where-Object {!$removed.Contains($_._id)})
 $original=Item 'abcd00000000000000000001' 30 $inv.stash 0
 $inv.items+=@($original)
 $p|ConvertTo-Json -Depth 100 -Compress|Set-Content "$TestRoot/user/profiles/$a.json"
 @{Schema=1;Revision=0;Items=@();History=@();Pending=$null}|ConvertTo-Json -Depth 100|Set-Content $statePath
 Start-Test
 $s=Read-Spt $a;Check $s.historyAvailable 'Public history button flag missing'
 Check ((Read-History).total -eq 0) 'History should start empty'
 Operation @{direction='deposit';itemId=$original._id;amount=5;revision=$s.revision}
 $s=Read-Spt $a;$shared=$s.items[0].id
 Operation @{direction='move';itemId=$shared;x=2;y=1;revision=$s.revision}
 $s=Read-Spt $a
 Operation @{direction='move';itemId=$shared;amount=2;x=3;y=1;revision=$s.revision}
 $s=Read-Spt $a;$part=($s.items|Where-Object id -ne $shared).id
 $null=Merge $a $part $false $shared $false $s.revision
 $s=Read-Spt $a
 Operation @{direction='withdraw';itemId=$shared;amount=1;revision=$s.revision}
 $s=Read-Spt $a
 Operation @{direction='withdraw';itemId=$shared;revision=$s.revision}
 $s=Read-Spt $a
 $h=Read-History;Check (!$h.error -and $h.total -eq 3) ('Transfer history wrong: '+($h|ConvertTo-Json -Compress -Depth 8))
 Check ($h.rows[0].action -eq 'withdraw' -and $h.rows[0].count -eq 4 -and $h.rows[1].count -eq 1 -and $h.rows[2].count -eq 5) 'Wrong actual split/withdraw amounts'
 Check ($h.rows[0].category -eq 'Ammo > Rounds') 'History ammo category missing'
 Check ($h.rows[0].player -eq $p.characters.pmc.Info.Nickname) 'Actor name is not server-authoritative'
 Check ((Read-History arrange).total -eq 3) 'Move/split/merge arrangement history missing'
 Check ((Read-History deposit).total -eq 1 -and (Read-History withdraw).total -eq 2) 'Action filters wrong'
 Check ((Read-History all 0 $p.characters.pmc.Info.Nickname).total -eq 3) 'Player search wrong'
 Check ((Read-History all -1).error) 'Invalid paging accepted'
 Check ((Read-History all 0 ('a'*81)).error) 'Oversized search accepted'
 Check ((Read-History all 0 '' '999999999999999999999999').error) 'Unknown session read history'
 Check (!(($h|ConvertTo-Json -Depth 20) -match $a)) 'Public response leaks profile ID'
 Operation @{direction='withdraw';itemId=$shared;revision=$s.revision}
 Check ((Read-History).total -eq 3) 'Rejected operation logged as success'
 $files=@(Get-ChildItem $historyPath -Filter '*.json');Check ($files.Count -eq 6) 'Unexpected archive count'
 $events=@($files|ForEach-Object {Get-Content $_.FullName -Raw|ConvertFrom-Json})
 Check (@($events|Where-Object PlayerId -eq $a).Count -eq 6) 'Server archive missing stable actor ID'
 Stop-Test;Start-Test
 Check ((Read-History).total -eq 3 -and @(Get-ChildItem $historyPath -Filter '*.json').Count -eq 6) 'Restart duplicates or loses history'
 Stop-Test;HistoryConfig $true $false;Start-Test
 Check (!(Read-Spt $a).historyAvailable -and (Read-History).error) 'Private mode exposes history API'
 $s=Read-Spt $a;Operation @{direction='deposit';itemId=$original._id;amount=1;revision=$s.revision}
 Check (@(Get-ChildItem $historyPath -Filter '*.json').Count -eq 7) 'Private mode stopped recording'
 Stop-Test;HistoryConfig $false $true;Start-Test
 $s=Read-Spt $a;Check (!$s.historyAvailable -and (Read-History).error) 'Disabled mode exposes history'
 Operation @{direction='deposit';itemId=$original._id;amount=1;revision=$s.revision}
 Check (@(Get-ChildItem $historyPath -Filter '*.json').Count -eq 7) 'Disabled history recorded new events'
 'PASS: real API history captures committed full/partial deposits/withdrawals and arrangement operations with authoritative actor and quantities; filters, search, input bounds, session checks, restart, private and disabled modes'
}finally{
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig);[IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 if(Test-Path -LiteralPath $historyPath){Move-Item -LiteralPath $historyPath -Destination $archiveResult}
 if($hadHistory){Move-Item -LiteralPath $backup -Destination $historyPath}
 'Isolated test server stopped; copied fixtures restored; history test output retained separately.'
}