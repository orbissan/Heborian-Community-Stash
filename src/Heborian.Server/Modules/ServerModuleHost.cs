using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Heborian.Server.Configuration;

namespace Heborian.Server.Modules;

public sealed class ServerModuleHost
{
    private bool started;

    public async Task StartAsync(IEnumerable<IServerModule> modules, ServerConfiguration config,
        Action<string> log, CancellationToken cancellationToken)
    {
        if (started) return;
        started = true;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(module.Id) || !ids.Add(module.Id))
                    throw new InvalidOperationException("Empty or duplicate module ID: " + module.Id);
                if (!module.IsEnabled(config))
                {
                    log($"[Heborian.Server] Disabled: {module.Id}");
                    continue;
                }
                await module.InitializeAsync(config, cancellationToken);
                log($"[Heborian.Server] Loaded: {module.Id}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // A broken independent module must not prevent the others from starting.
                // Each module must validate before mutations; this is not a global rollback.
                log($"[Heborian.Server] FAILED: {module.Id}: {error}");
            }
        }
    }
}
