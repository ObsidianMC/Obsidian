using System.Collections.Immutable;

namespace Obsidian.API;

public interface IOperatorList
{
    public void AddOperator(IPlayer player, int level = 4, bool bypassesPlayerLimit = false);
    public bool CreateRequest(IPlayer player);
    public bool ProcessRequest(IPlayer player, string code);
    public void RemoveOperator(IPlayer player);
    public bool IsOperator(IPlayer p);
    public ImmutableList<IPlayer> GetOnlineOperators();
}
