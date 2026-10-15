using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heborian.Server.Configuration;
using Heborian.Server.Modules;
using Heborian.Server.Modules.MainMenuBanner;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Heborian.Server;

[Injectable(InjectionType.Singleton, OnLoadOrder.PostLoad + 100)]
public sealed class HeborianServer : IOnLoad
{
    private readonly IServerModule[] modules;
    private readonly ServerModuleHost host = new();

    // Explicit registration: no assembly scanning or additional plugin framework.
    public HeborianServer(MainMenuBannerModule banner, Modules.CommunityWarehouse.WarehouseModule warehouse) => modules = new IServerModule[] { banner, warehouse };

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        ServerConfiguration configuration;
        try
        {
            var directory = Path.GetDirectoryName(typeof(HeborianServer).Assembly.Location)!;
            configuration = ServerConfiguration.Load(Path.Combine(directory, "config.json"));
        }
        catch (Exception error)
        {
            Console.WriteLine($"[Heborian.Server] Configuration error; no modules started. {error}");
            return;
        }
        await host.StartAsync(modules, configuration, Console.WriteLine, cancellationToken);
    }
}
