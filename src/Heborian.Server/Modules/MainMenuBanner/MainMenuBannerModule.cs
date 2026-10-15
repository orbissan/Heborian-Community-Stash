using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heborian.Server.Configuration;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Heborian.Server.Modules.MainMenuBanner;

[Injectable(InjectionType.Singleton)]
public sealed class MainMenuBannerModule : IServerModule
{
    public const string TitleKey = "Attention! This is a Beta version of Escape from Tarkov for testing purposes.";
    public const string DescriptionKey = "NDA free warning";
    public const string NdaDescriptionKey = "NDA Policy warning";
    // EFT.UI.LocalizedText.UpdateLocale returns before SetLabelText for an empty string.
    // A space clears the prefab's original text while passing that IsNullOrEmpty guard.
    public const string BlankDescription = " ";
    private readonly LocaleTable locales;
    private bool registered;

    public MainMenuBannerModule(LocaleTable locales) => this.locales = locales;
    public string Id => "MainMenuBanner";
    public bool IsEnabled(ServerConfiguration config) => GetOptions(config).Enabled;

    public Task InitializeAsync(ServerConfiguration config, CancellationToken cancellationToken)
    {
        if (registered || !IsEnabled(config)) return Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        var title = GetOptions(config).Title;
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("MainMenuBanner.Title must not be empty.");
        if (locales.Global == null || locales.Global.Count == 0)
            throw new InvalidOperationException("SPT global locales have not been loaded.");
        foreach (var locale in locales.Global.Values)
        {
            // Snapshot configuration. SPT invalidates existing caches; no eager loading or file edits.
            locale.AddTransformer(dictionary => Transform(dictionary, BuildInfo.BannerTitle(title)));
        }
        registered = true;
        Console.WriteLine($"[Heborian.Server] MainMenuBanner: registered for {locales.Global.Count} locales.");
        return Task.CompletedTask;
    }

    private static MainMenuBannerOptions GetOptions(ServerConfiguration config) =>
        config.MainMenuBanner ?? throw new InvalidDataException("MainMenuBanner must be a JSON object.");

    public static GlobalLocaleDictionary Transform(GlobalLocaleDictionary locale, string title)
    {
        locale[TitleKey] = title;
        locale[DescriptionKey] = BlankDescription;
        locale[NdaDescriptionKey] = BlankDescription;
        return locale;
    }
}
