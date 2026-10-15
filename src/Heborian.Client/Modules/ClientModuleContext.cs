using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using Heborian.Client.Resources;

namespace Heborian.Client.Modules
{
    public sealed class ClientModuleContext : IDisposable
    {
        private readonly ConfigFile configuration;
        public string Id { get; }
        public ManualLogSource Log { get; }
        public ModuleAssets Assets { get; }

        public ClientModuleContext(string id, ConfigFile configuration, ManualLogSource log, string assetRoot)
        {
            Id = id;
            this.configuration = configuration;
            Log = log;
            Assets = new ModuleAssets(assetRoot);
        }

        public ConfigEntry<T> Bind<T>(string key, T defaultValue, string description) =>
            configuration.Bind("Modules." + Id, key, defaultValue, description);

        public void Dispose() => Assets.Dispose();
    }
}
