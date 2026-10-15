using System.Threading;
using System.Threading.Tasks;
using Heborian.Server.Configuration;

namespace Heborian.Server.Modules;

/// <summary>Constructors have no side effects. Changes are applied once, at startup.</summary>
public interface IServerModule
{
    string Id { get; }
    bool IsEnabled(ServerConfiguration configuration);
    Task InitializeAsync(ServerConfiguration configuration, CancellationToken cancellationToken);
}
