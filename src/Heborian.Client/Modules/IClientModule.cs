using System;

namespace Heborian.Client.Modules
{
    /// <summary>Constructor has no side effects. Dispose must also handle partial initialization.</summary>
    public interface IClientModule : IDisposable
    {
        string Id { get; }
        void Initialize(ClientModuleContext context);
    }
}
