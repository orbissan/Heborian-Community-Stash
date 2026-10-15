using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Heborian.Server;

public sealed class ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "local.heborian";
    public string Name { get; init; } = "Heborian.Server";
    public string Author { get; init; } = "Heborian";
    public List<string> Contributors { get; init; } = new();
    public SemanticVersioning.Version Version { get; init; } = new(BuildInfo.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new("=4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string> Incompatibilities { get; init; } = new() { "com.kittehkun.welcomemessages" };
    public Dictionary<string, SemanticVersioning.Range> ModDependencies { get; init; } = new();
    public string Url { get; init; } = "";
    public string License { get; init; } = "MIT";
}
