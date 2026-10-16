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
try {
 $current=& "$PSScriptRoot/Get-Version.ps1"
 foreach($enabled in @($false,$true)) {
  Configure $enabled
  $p=$originalProfiles[$a] | ConvertFrom-Json -Depth 100
  $inv=$p.characters.pmc.Inventory
  $case=@{_id='ddddddddddddddddddddddd1';_tpl='5857a8b324597729ab0a0e7d';parentId=$inv.stash;slotId='hideout';location=@{x=6;y=12;r=1;isSearched=$true};upd=@{StackObjectsCount=1}}
  $child=Item 'ddddddddddddddddddddddd2' 12 $case._id 0
  $child.slotId='main'
  $inv.items+=@($case,$child)
  # Exercise folded weapon dimensions while retaining native attachments.
  $db=Get-Content "$TestRoot/SPT_Data/database/templates/items.json" -Raw | ConvertFrom-Json -AsHashtable -Depth 100
  foreach($item in $inv.items) {if($db[$item._tpl]._props.Foldable){
   if(!$item.upd){$item|Add-Member upd ([pscustomobject]@{}) -Force}
   $item.upd|Add-Member Foldable ([pscustomobject]@{Folded=$true}) -Force
  }}
  $p | ConvertTo-Json -Depth 100 -Compress | Set-Content "$TestRoot/user/profiles/$a.json"
  $baseline=@{}
  foreach($version in @('1.10.2',$current)) {
   $ModVersion=$version
   Start-Test
   foreach($id in @($a,$b)) {
    $s=Read-Spt $id;Check (!$s.error) $s.error
    # Compare inventory data; 1.11 adds a UI capability absent from 1.10.2.
    $s.PSObject.Properties.Remove('historyAvailable')
    $data=$s|ConvertTo-Json -Depth 100 -Compress
    if($version -eq '1.10.2'){$baseline[$id]=$data}
    else{Check ($baseline[$id] -ceq $data) 'Optimized snapshot differs from baseline item data, eligibility or size'}
   }
   Stop-Test
  }
  "PASS: complete snapshot matches 1.10.2 for both profiles, deposit categories enabled=$enabled, nested container and folded equipment"
 }
}finally{
 Stop-Test
 [IO.File]::WriteAllText($configPath,$originalConfig)
 [IO.File]::WriteAllText($statePath,$originalState)
 foreach($id in $originalProfiles.Keys){[IO.File]::WriteAllText("$TestRoot/user/profiles/$id.json",$originalProfiles[$id])}
 'Isolated test server stopped; copied fixtures restored.'
}
