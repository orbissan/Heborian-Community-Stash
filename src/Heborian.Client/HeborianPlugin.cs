using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using Heborian.Client.Modules;
using Heborian.Client.Modules.HeborianMenu;

namespace Heborian.Client
{
    [BepInPlugin(PluginId, "Heborian.Client", BuildInfo.Version)]
    [BepInDependency("com.SPT.core", "4.1.6")]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class HeborianPlugin : BaseUnityPlugin
    {
        public const string PluginId = "local.heborian.client";
        private ClientModuleHost host;

        private void Awake()
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.SPT.core", out var spt)
                || !Compatibility.IsSupported(spt.Metadata.Version))
            {
                Logger.LogError("Heborian.Client requires exactly SPT 4.1.6; no modules started.");
                return;
            }
            if (!Config.Bind("General", "Enabled", true, "Enable Heborian.Client. Restart the game after changes.").Value)
            {
                Logger.LogInfo("Heborian.Client disabled.");
                return;
            }
            var directory = Path.GetDirectoryName(Info.Location);
            host = new ClientModuleHost(Config, Logger, Path.Combine(directory, "assets"));
            host.Start(new IClientModule[] { new HeborianMenuModule(), new Modules.CommunityWarehouse.WarehouseClientModule() });
            Logger.LogInfo("Heborian.Client " + BuildInfo.Version + " ready for SPT 4.1.6.");
        }

        private void OnDestroy()
        {
            host?.Dispose();
            host = null;
        }
    }

    public static class Compatibility
    {
        public static bool IsSupported(Version version) => version != null
            && version.Major == 4 && version.Minor == 1 && version.Build == 6
            && (version.Revision == -1 || version.Revision == 0);
    }
}
