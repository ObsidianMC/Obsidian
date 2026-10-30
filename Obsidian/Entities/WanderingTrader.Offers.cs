using Obsidian.WorldData.Maps;

namespace Obsidian.Entities;

public sealed partial class WanderingTrader
{
    // Vanilla's WanderingTrader.updateTrades order.
    private static readonly string[] tradeSets =
    [
        VillagerTrades.WanderingTraderBuying,
        VillagerTrades.WanderingTraderUncommon,
        VillagerTrades.WanderingTraderCommon
    ];

    private ImmutableArray<TradeEntry> offers = [];

    private void CreateOffers()
    {
        var sequences = BlockLoot.SequencesFor(Level);
        var explorationMaps = ExplorationMaps.For(Level);

        var drawn = new List<TradeEntry>();
        foreach (var tradeSet in tradeSets)
        {
            VillagerTrades.AddOffers(tradeSet, this, sequences, explorationMaps, drawn);
        }

        offers = [.. drawn];
    }
}
