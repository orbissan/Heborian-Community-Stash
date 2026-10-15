using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Heborian.Server.Configuration;
using Heborian.Server.Modules.MainMenuBanner;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

public static class LocaleVerification
{
    public static string Run(string runtime)
    {
        int checks = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(runtime, "SPT_Data", "database", "locales", "global"), "*.json"))
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            // Use SPT's case-insensitive dictionary semantics for the baseline too.
            var original = new GlobalLocaleDictionary();
            foreach (var pair in raw) original[pair.Key] = pair.Value;
            if (!original.ContainsKey(MainMenuBannerModule.TitleKey) || !original.ContainsKey(MainMenuBannerModule.DescriptionKey))
                throw new Exception("Missing banner keys: " + file);
            foreach (bool warmCache in new[] { false, true })
            {
                int loads = 0;
                var lazy = new LazyLoad<GlobalLocaleDictionary>(() =>
                {
                    loads++;
                    var result = new GlobalLocaleDictionary();
                    foreach (var pair in original) result[pair.Key] = pair.Value;
                    return result;
                }, true);
                var table = new LocaleTable
                {
                    Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>> { ["test"] = lazy },
                    Menu = new Dictionary<string, Dictionary<string, object>>(),
                    Languages = new Dictionary<string, string>()
                };
                if (warmCache) { var ignored = lazy.Value; }
                var feature = new MainMenuBannerModule(table);
                feature.InitializeAsync(new ServerConfiguration(), CancellationToken.None).GetAwaiter().GetResult();
                if (loads != (warmCache ? 1 : 0)) throw new Exception("Feature eagerly loaded locales.");
                var actual = lazy.Value;
                if (actual[MainMenuBannerModule.TitleKey] != "Heborian " + Heborian.BuildInfo.Version)
                    throw new Exception("Banner mismatch: " + file);
                foreach (var key in new[] { MainMenuBannerModule.DescriptionKey, MainMenuBannerModule.NdaDescriptionKey })
                {
                    // Regression: an empty translation makes LocalizedText.UpdateLocale return,
                    // retaining the Russian text serialized in the UI prefab. Blank must be nonempty.
                    if (string.IsNullOrEmpty(actual[key]) || !string.IsNullOrWhiteSpace(actual[key]))
                        throw new Exception("Description must clear the label without triggering EFT's empty-text guard: " + file);
                }
                foreach (var pair in original)
                {
                    if (pair.Key == MainMenuBannerModule.TitleKey || pair.Key == MainMenuBannerModule.DescriptionKey || pair.Key == MainMenuBannerModule.NdaDescriptionKey) continue;
                    if (actual[pair.Key] != pair.Value) throw new Exception("Unrelated locale changed: " + pair.Key);
                }
                feature.InitializeAsync(new ServerConfiguration(), CancellationToken.None).GetAwaiter().GetResult();
                if (!ReferenceEquals(actual, lazy.Value)) throw new Exception("Feature registered twice.");
                checks++;
            }
        }
        return $"PASS: {checks} real locale/cache cases; banner text correct, other translations unchanged.";
    }
}
