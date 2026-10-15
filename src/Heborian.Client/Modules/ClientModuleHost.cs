using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Heborian.Client.Modules
{
    public sealed class ClientModuleHost : IDisposable
    {
        private readonly ConfigFile config;
        private readonly ManualLogSource log;
        private readonly string assetRoot;
        private readonly List<Tuple<IClientModule, ClientModuleContext>> active = new List<Tuple<IClientModule, ClientModuleContext>>();
        private bool started;
        private bool disposed;

        public ClientModuleHost(ConfigFile config, ManualLogSource log, string assetRoot)
        {
            this.config = config;
            this.log = log;
            this.assetRoot = assetRoot;
        }

        public void Start(IEnumerable<IClientModule> modules)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ClientModuleHost));
            if (started) return;
            started = true;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules)
            {
                ClientModuleContext context = null;
                try
                {
                    if (module.Id == null || !Regex.IsMatch(module.Id, "^[A-Za-z][A-Za-z0-9_]*$") || !ids.Add(module.Id))
                        throw new InvalidOperationException("Invalid or duplicate module ID: " + module.Id);
                    context = new ClientModuleContext(module.Id, config, log, Path.Combine(assetRoot, module.Id));
                    if (!context.Bind("Enabled", true, "Enable this module. Restart the game after changes.").Value)
                    {
                        log.LogInfo("Disabled: " + module.Id);
                        Release(module, context);
                        continue;
                    }
                    module.Initialize(context);
                    active.Add(Tuple.Create(module, context));
                    log.LogInfo("Loaded: " + module.Id);
                }
                catch (Exception error)
                {
                    log.LogError("Module failed: " + module.Id + ": " + error);
                    Release(module, context);
                }
            }
        }

        private void Release(IClientModule module, ClientModuleContext context)
        {
            try { module.Dispose(); }
            catch (Exception error) { log.LogError("Module cleanup failed: " + module.Id + ": " + error); }
            finally
            {
                try { context?.Dispose(); }
                catch (Exception error) { log.LogError("Asset cleanup failed: " + module.Id + ": " + error); }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = active.Count - 1; i >= 0; i--) Release(active[i].Item1, active[i].Item2);
            active.Clear();
        }
    }
}
