using System.Threading;

namespace Obsidian.Hosting;
public interface IServerSetup
{
    Task<IServerConfiguration> LoadServerConfiguration(CancellationToken cToken);
    Task<List<ServerWorld>> LoadServerWorlds(CancellationToken cToken);
}
