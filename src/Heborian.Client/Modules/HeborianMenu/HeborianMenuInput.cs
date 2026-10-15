using System;
using EFT.InputSystem;

namespace Heborian.Client.Modules.HeborianMenu
{
    /// <summary>Registered last in the game's input tree, so Escape never reaches the menu below.</summary>
    public sealed class HeborianMenuInput : InputNode
    {
        public Action Back;
        public static Func<bool> CancelModal;
        public override ETranslateResult TranslateCommand(ECommand command)
        {
            if (command == ECommand.Escape && CancelModal?.Invoke() != true) Back?.Invoke();
            return ETranslateResult.BlockAll;
        }
        public override void TranslateAxes(ref float[] axes) { if (axes != null) Array.Clear(axes, 0, axes.Length); }
        public override ECursorResult ShouldLockCursor() => ECursorResult.ShowCursor;
    }
}
