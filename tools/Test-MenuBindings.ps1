param([string]$SptRoot)
$ErrorActionPreference = 'Stop'
# Read-only ABI checks against the actual game DLL; no Unity objects or game process started.
Add-Type -Path (Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $bar = $game.MainModule.GetType('EFT.UI.MenuTaskBar')
    $toggles = $bar.Fields | Where-Object Name -EQ '_toggleButtons'
    if ($toggles.FieldType.FullName -ne 'System.Collections.Generic.Dictionary`2<EFT.UI.EMenuType,EFT.UI.AnimatedToggle>') { throw 'Unexpected taskbar toggle field.' }
    $pressed = $bar.Fields | Where-Object Name -EQ 'OnTaskBarButtonPressed'
    if ($pressed.FieldType.FullName -ne 'System.Action`2<EFT.UI.EMenuType,System.Boolean>') { throw 'Unexpected taskbar click delegate.' }
    $back = $game.MainModule.GetType('EFT.UI.InventoryScreen').Fields | Where-Object Name -EQ '_backButton'
    if ($back.FieldType.FullName -ne 'EFT.UI.DefaultUIButton') { throw 'Unexpected native BACK template.' }
    $tree = $game.MainModule.GetType('EFT.InputSystem.InputTree')
    if (!($tree.Methods | Where-Object { $_.Name -eq 'get_InputManager' -and $_.IsPublic })) { throw 'InputTree.InputManager unavailable.' }
    $manager = $game.MainModule.GetType('EFT.InputSystem.InputManager')
    if (!($manager.Methods | Where-Object { $_.Name -eq 'get_TranslateDelegate' -and $_.IsPublic })) { throw 'InputManager.TranslateDelegate unavailable.' }
    $awake = $tree.Methods | Where-Object Name -EQ 'Awake'
    if (!($awake.Body.Instructions | Where-Object { "$($_.Operand)" -match 'InputManager::set_TranslateDelegate' })) { throw 'InputTree no longer binds the input delegate.' }
    if (!($game.MainModule.GetType('EFT.UI.EMenuType').Fields | Where-Object Name -EQ 'Player')) { throw 'Character tab identifier missing.' }
    foreach ($name in @('Add','Remove')) {
        if (!($tree.Methods | Where-Object { $_.Name -eq $name -and $_.IsPublic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.InputSystem.InputNode' })) { throw "InputTree.$name contract changed." }
    }
    # Escape consumes the command batch; cursor remains visible using these actual enum values.
    $command = $game.MainModule.GetType('EFT.InputSystem.ECommand').Fields | Where-Object Name -EQ 'Escape'
    if ($null -eq $command) { throw 'Escape command missing.' }
    $itemUi=$game.MainModule.GetType('EFT.UI.ItemUiContext')
    $splitTemplate=$itemUi.Fields | Where-Object Name -EQ '_splitDialogTemplate'
    if($splitTemplate.FieldType.FullName -ne 'EFT.UI.SplitDialog') { throw 'Native split prefab contract changed.' }
    if(!($itemUi.Methods | Where-Object { $_.Name -eq 'get_Instance' -and $_.IsPublic -and $_.IsStatic })) { throw 'ItemUiContext.Instance unavailable.' }
    $splitGetter=$itemUi.Methods | Where-Object Name -EQ 'get_SplitDialog'
    if(!($splitGetter.Body.Instructions | Where-Object { "$($_.Operand)" -match 'ItemUiContext::_splitDialogTemplate' })) { throw 'Native lazy split source changed.' }
    Write-Output 'PASS: native split prefab binding exists before lazy scene-dialog creation.'
    $step=$game.MainModule.GetType('EFT.UI.StepSlider')
    if(($step.Fields | Where-Object Name -EQ '_currentValue').FieldType.FullName -ne 'System.Int32') { throw 'Exact StepSlider integer binding missing.' }
    if(($game.MainModule.GetType('EFT.UI.SplitDialog').Fields | Where-Object Name -EQ '_stepSlider').FieldType.FullName -ne 'EFT.UI.StepSlider') { throw 'Step dialog binding missing.' }
    $stepShow=$step.Methods | Where-Object Name -EQ 'Show'
    if(!($stepShow.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldc_I4_S' -and $_.Operand -eq 24 })) { throw 'Native bounded step count changed; review allocation safety.' }
    Write-Output 'PASS: bounded native StepSlider and exact integer amount bindings.'
    $mainImage=$game.MainModule.GetType('EFT.UI.DragAndDrop.ItemView').Fields | Where-Object Name -EQ 'MainImage'
    if($mainImage.FieldType.FullName -ne 'UnityEngine.UI.Image') { throw 'Native item icon binding changed.' }
    Write-Output 'PASS: native item image available for icon-only drag presentation.'
    $firPanel=$game.MainModule.GetType('EFT.UI.DragAndDrop.QuestItemViewPanel')
    foreach($field in @('_foundInRaidSprite','_questIconImage','_questItemLabel')) {
        if(!($firPanel.Fields | Where-Object Name -EQ $field)) { throw "Native FIR field missing: $field" }
    }
    Write-Output 'PASS: native FIR sprite/image/label bindings for detached warehouse item views.'
    Write-Output 'PASS: real SPT 4.1.6 Character tab, native BACK, click delegate and live input-manager/tree bindings.'
} finally { $game.Dispose() }
