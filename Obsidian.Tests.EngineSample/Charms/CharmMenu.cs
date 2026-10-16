using Obsidian.API;
using Obsidian.API.Data;

namespace EngineSample.Charms;

/// <summary>
/// The <c>sample:charms</c> menu: a player's own charm slots, which take one charm each and nothing else. It's open
/// until the player closes it, like their inventory, and needs no block entity.
/// </summary>
public sealed class CharmMenu : MenuContainer
{
    public CharmMenu(MenuDefinition menu, IPlayer player)
        : base(menu, new ChatMessage { Translate = "container.sample.charms" })
    {
        // The attachment is the only copy of the items: each slot reads and changes it in one step.
        var worn = player.GetItemAccess(CharmSlots.Worn, CharmSlots.SlotCount);

        this.Slots =
        [
            .. Enumerable.Range(0, CharmSlots.SlotCount).Select(slot => new MenuSlot(worn, slot)
            {
                MayPlace = item => item.Is(CharmSlots.Charm),
                MaxStackSize = 1
            })
        ];
    }
}
