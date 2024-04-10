using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;

namespace Obsidian.API;

public interface IServer : IDisposable
{
    public string Version { get; }
    public int Port { get; }
    public int Tps { get; }
    public DateTimeOffset StartTime { get; }
    public ProtocolVersion Protocol { get; }
    public IEnumerable<IPlayer> Players { get; }
    public IOperatorList Operators { get; }
    public IWorld DefaultWorld { get; }
    public ServerConfiguration Configuration { get; }

    public IScoreboardManager ScoreboardManager { get; }

    public Task RunAsync();

    public bool IsPlayerOnline(string username);
    public bool IsPlayerOnline(Guid uuid);
    public void BroadcastMessage(string message);
    public void BroadcastMessage(ChatMessage message);
    public IPlayer? GetPlayer(string username);
    public IPlayer? GetPlayer(Guid uuid);
    public IPlayer? GetPlayer(int entityId);
    public void RegisterRecipes(params IRecipe[] recipes);

    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags);
}
