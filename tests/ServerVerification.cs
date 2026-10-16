using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heborian.Server.Configuration;
using Heborian.Server.Modules;
using Heborian.Server.Modules.MainMenuBanner;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

public static class ServerVerification
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Run(string temp)
    {
        // A broken framework response must be rejected before profile access/journaling.
        var responseGuard=new Heborian.Server.Modules.CommunityWarehouse.WarehouseModule(null,null,null,null,null);
        foreach(var output in new SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse[] {
            null, new SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse() })
        {
            bool rejected=false;
            try { responseGuard.Transfer(null,null,default,output).GetAwaiter().GetResult(); }
            catch(InvalidOperationException e) { rejected=e.Message.StartsWith("Inventory response unavailable."); }
            Check(rejected,"Missing inventory output reached profile mutation path");
        }
        Console.WriteLine("PASS: missing/malformed native inventory responses rejected before any profile access or journal write.");
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, Guid.NewGuid() + ".json");
        var defaults = ServerConfiguration.Load(path);
        Check(defaults.MainMenuBanner.Enabled && defaults.MainMenuBanner.Title == "Heborian", "Wrong defaults");
        File.WriteAllText(path, "{\"MainMenuBanner\":{\"Enabled\":false,\"Title\":\"Custom title\"}}");
        var bytes = File.ReadAllText(path);
        var custom = ServerConfiguration.Load(path);
        Check(!custom.MainMenuBanner.Enabled && custom.MainMenuBanner.Title == "Custom title", "Config ignored");
        Check(File.ReadAllText(path) == bytes, "Existing config overwritten");
        foreach (var invalid in new[] { "{", "null", "{\"MainMenuBanner\":{\"Enabeld\":false}}" })
        {
            File.WriteAllText(path, invalid);
            bool rejected = false;
            try { ServerConfiguration.Load(path); } catch (Exception e) when (e is JsonException || e is InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(path) == invalid, "Invalid config silently accepted or overwritten");
        }
        Check(defaults.CommunityWarehouse.AllowedDeposits.Food && defaults.CommunityWarehouse.AllowedDeposits.Ammo
            && !defaults.CommunityWarehouse.AllowedDeposits.Weapons, "Wrong deposit defaults");
        File.WriteAllText(path, "{\"CommunityWarehouse\":{\"AllowedDeposits\":{\"Weapons\":true,\"Food\":false}}}");
        var categories=ServerConfiguration.Load(path).CommunityWarehouse.AllowedDeposits;
        Check(categories.Weapons && !categories.Food && categories.Ammo, "Category overrides or omitted defaults ignored");
        File.WriteAllText(path, "{\"CommunityWarehouse\":{\"AllowedDeposits\":{\"Wepons\":true}}}");
        bool categoryTypo=false;
        try { ServerConfiguration.Load(path); } catch(JsonException) { categoryTypo=true; }
        Check(categoryTypo,"Misspelled category silently accepted");
        Check(defaults.CommunityWarehouse.History.Enabled && defaults.CommunityWarehouse.History.Public
            && defaults.CommunityWarehouse.History.RetentionDays==30,"Wrong history defaults");
        File.WriteAllText(path,"{\"CommunityWarehouse\":{\"History\":{\"Enabled\":true,\"Public\":false,\"RetentionDays\":90}}}");
        var history=ServerConfiguration.Load(path).CommunityWarehouse.History;
        Check(history.Enabled && !history.Public && history.RetentionDays==90,"History options ignored");
        var original = new GlobalLocaleDictionary { [MainMenuBannerModule.TitleKey] = "Original", [MainMenuBannerModule.DescriptionKey] = "Description" };
        var lazy = new LazyLoad<GlobalLocaleDictionary>(() => original, true);
        var table = new LocaleTable
        {
            Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>> { ["test"] = lazy },
            Menu = new Dictionary<string, Dictionary<string, object>>(), Languages = new Dictionary<string, string>()
        };
        var banner = new MainMenuBannerModule(table);
        banner.InitializeAsync(custom, CancellationToken.None).GetAwaiter().GetResult();
        Check(lazy.Value[MainMenuBannerModule.TitleKey] == "Original", "Disabled banner mutated locale");
        custom.MainMenuBanner.Enabled = true;
        banner.InitializeAsync(custom, CancellationToken.None).GetAwaiter().GetResult();
        custom.MainMenuBanner.Title = "Changed after initialization";
        Check(lazy.Value[MainMenuBannerModule.TitleKey] == "Custom title " + Heborian.BuildInfo.Version, "Configured title/version not snapshotted");
        Check(new Heborian.Server.ModMetadata().Version.ToString() == Heborian.BuildInfo.Version, "Metadata/banner version mismatch");

        var logs = new List<string>();
        var disabled = new Fake("Disabled", false);
        var failed = new Fake("Failed", true, true);
        var good = new Fake("Good", true);
        var duplicate = new Fake("Good", true);
        var host = new ServerModuleHost();
        var modules = new IServerModule[] { disabled, failed, good, duplicate };
        host.StartAsync(modules, defaults, logs.Add, CancellationToken.None).GetAwaiter().GetResult();
        host.StartAsync(modules, defaults, logs.Add, CancellationToken.None).GetAwaiter().GetResult();
        Check(disabled.Calls == 0 && failed.Calls == 1 && good.Calls == 1 && duplicate.Calls == 0, "Module isolation/idempotence failed");
        Check(logs.Exists(x => x.Contains("FAILED: Failed")) && logs.Exists(x => x.Contains("Loaded: Good")), "Missing diagnostic logs");
        var cancelled = new CancellationToken(true);
        bool propagated = false;
        try { new ServerModuleHost().StartAsync(modules, defaults, logs.Add, cancelled).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { propagated = true; }
        Check(propagated, "Cancellation swallowed");
        return "PASS: server defaults/custom config, invalid config, disabled/custom banner, isolated failures, duplicate IDs, idempotence and cancellation.";
    }

    private sealed class Fake : IServerModule
    {
        public string Id { get; }
        private readonly bool enabled;
        private readonly bool fail;
        public int Calls;
        public Fake(string id, bool enabled, bool fail = false) { Id = id; this.enabled = enabled; this.fail = fail; }
        public bool IsEnabled(ServerConfiguration config) => enabled;
        public Task InitializeAsync(ServerConfiguration config, CancellationToken token)
        {
            Calls++;
            if (fail) throw new InvalidOperationException("Expected test failure");
            return Task.CompletedTask;
        }
    }
}
