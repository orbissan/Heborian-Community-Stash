using System;
using System.Collections.Generic;

namespace Heborian.Client.Modules.HeborianMenu
{
    // Content follows the screen that owns it, not an unrelated startup GameObject.
    // Kept independent of Unity so open/close/failure behaviour can be tested.
    public sealed class MenuContentLifecycle
    {
        public event Action Opened;
        public event Action Updated;
        public event Action Closed;
        public bool IsOpen { get; private set; }
        private Action<string, Exception> report;
        private readonly HashSet<Delegate> reported = new HashSet<Delegate>();

        public void Open(Action<string, Exception> onError)
        {
            if(IsOpen) return;
            report=onError;
            reported.Clear();
            IsOpen=true;
            Notify(Opened,"open");
        }
        public void Tick() { if(IsOpen) Notify(Updated,"update"); }
        public void Close()
        {
            if(!IsOpen) return;
            IsOpen=false;
            Notify(Closed,"close");
            report=null;
        }
        private void Notify(Action callbacks,string phase)
        {
            if(callbacks == null) return;
            foreach(Action callback in callbacks.GetInvocationList())
            {
                try { callback(); }
                catch(Exception error)
                {
                    // A broken module must not break BACK or flood the log every frame.
                    if(reported.Add(callback)) report?.Invoke(phase,error);
                }
            }
        }
    }
}
