using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Heborian.Server.Modules.MainMenuBanner;

namespace Heborian.Server.Configuration;

public sealed class ServerConfiguration
{
    public MainMenuBannerOptions MainMenuBanner { get; set; } = new();
    public Heborian.Server.Modules.CommunityWarehouse.WarehouseOptions CommunityWarehouse { get; set; } = new();

    public static ServerConfiguration Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Catch typos instead of silently enabling a module the user meant to disable.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        if (!File.Exists(path))
        {
            var defaults = new ServerConfiguration();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options) + Environment.NewLine);
            return defaults;
        }
        return JsonSerializer.Deserialize<ServerConfiguration>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Heborian config must contain a JSON object.");
    }
}
