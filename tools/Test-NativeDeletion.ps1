param([string]$SptRoot=(Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference='Stop'
$managed=Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed'
foreach($name in @('UnityEngine.CoreModule','UnityEngine','Comfort','Comfort.Unity','Diz.Binding','Diz.LanguageExtensions','Newtonsoft.Json','Assembly-CSharp')){if(Test-Path "$managed/$name.dll"){$null=[Reflection.Assembly]::LoadFrom("$managed/$name.dll")}}
$game=[Reflection.Assembly]::LoadFrom("$managed/Assembly-CSharp.dll")
$flags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
function New-Native($name){[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($game.GetType("EFT.InventoryLogic.$name",$true))}
$ownerMethod=$game.GetType('EFT.InventoryLogic.ContainerExtensions').GetMethod('GetOwner')
$current=$game.GetType('EFT.InventoryLogic.Item').GetField('<CurrentAddress>k__BackingField',$flags)
# No Unity scene or live profile: use inert native objects to exercise the real owner traversal.
# ManageDeletedItems enumerates a pre-deletion snapshot, so descendants remain in its work list.
foreach($depth in @(1,2,4)) {
 foreach($legacy in @($true,$false)) {
  $owner=New-Native 'ItemController';$container=$owner;$nodes=@();$addresses=@()
  for($i=0;$i -lt $depth;$i++) {
   $node=New-Native 'Stash'
   $address=New-Native 'Grid+ProtectedGridItemAddress'
   $address.GetType().GetField('Container').SetValue($address,$container)
   $current.SetValue($node,$address)
   $nodes+=@($node);$addresses+=@($address)
   $grid=New-Native 'Grid'
   $grid.GetType().GetField('_parentItem',$flags).SetValue($grid,$node)
   $grid.GetType().GetField('_iD',$flags).SetValue($grid,'main')
   $container=$grid
  }
  $failed=$false
  $count=if($legacy){$depth}else{1}
  for($i=0;$i -lt $count;$i++) {
   try{
    $actual=$ownerMethod.Invoke($null,@($addresses[$i]))
    if(![object]::ReferenceEquals($actual,$owner)){throw 'Wrong native owner'}
    # This is the detach performed by native Remove during the deletion pass.
    $current.SetValue($nodes[$i],$null)
   }catch{
    if($_.Exception.ToString() -notmatch 'Trying to get owner of discarded item'){throw}
    $failed=$true;break
   }
  }
  if($failed -ne ($legacy -and $depth -gt 1)){throw "Unexpected owner traversal result: depth=$depth legacy=$legacy"}
 }
 "PASS: native owner traversal depth=$depth; old descendant deletion fails only for filled trees; root-only deletion succeeds"
}
