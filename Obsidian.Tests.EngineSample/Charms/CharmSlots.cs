using Obsidian.API;
using Obsidian.API.Client;
using Obsidian.API.Data;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Networking;
using Obsidian.API.Plugins;

namespace EngineSample.Charms;

/// <summary>
/// Phase 5's player extension: two charm slots each player keeps, in an item attachment, <c>sample:charms</c>, which
/// a menu (<see cref="CharmMenu"/>) shows next to their inventory. A key on the Obsidian client (K by default) opens
/// it, through a payload (<see cref="OpenCharms"/>). Each <c>sample:charm</c> worn, which moths drop, gives the player
/// a jump in the air.
/// </summary>
/// <remarks>
/// The attachment is saved with the player and dropped at a death, like the inventory, unless
/// <c>keep_inventory</c> is on. Movement abilities aren't saved, so they're granted again as the player joins.
/// </remarks>
public static class CharmSlots
{
    /// <summary>
    /// The menu type's identifier, which the client's screen is registered for.
    /// </summary>
    public const string MenuIdentifier = "sample:charms";

    /// <summary>
    /// How many charm slots players have.
    /// </summary>
    public const int SlotCount = 2;

    // The id the charms' movement abilities are granted under.
    private const string AbilitiesId = "sample:charms";

    /// <summary>
    /// The <c>sample:charm</c> item, the only one the slots take.
    /// </summary>
    public static Item Charm { get; private set; } = null!;

    /// <summary>
    /// The charms a player wears, in the <c>sample:charms</c> attachment.
    /// </summary>
    public static AttachmentType<ItemStack?[]> Worn { get; private set; } = null!;

    /// <summary>
    /// The <c>sample:charms</c> menu type.
    /// </summary>
    public static MenuDefinition Menu { get; private set; } = null!;

    /// <summary>
    /// Registers the item, the attachment, the menu type and the payload on both sides, and, on the server, what
    /// grants the charms' jumps.
    /// </summary>
    public static void Register(IPluginRegistry registry)
    {
        Charm = registry.RegisterItem("sample:charm", new ItemProperties { MaxStackSize = 1 });

        // The menu syncs the slots while it's open, so the attachment needs no network codec.
        Worn = registry.RegisterAttachment("sample:charms", ItemAttachments.Codec);
        Worn.Changed += holder =>
        {
            if (holder is IPlayer player)
                Apply(player);
        };

        Menu = registry.RegisterMenu(MenuIdentifier, rows: 1, columns: SlotCount);
        registry.MapPayload<OpenCharms>(PayloadPhase.Play, PayloadDirection.Serverbound, OpenAsync);
        registry.MapEvent<PlayerJoinEventArgs>(JoinedAsync);
    }

    /// <summary>
    /// Registers the client's key binding, which asks the server to open the menu, and the menu's screen.
    /// </summary>
    public static void RegisterClient(IClientRegistry registry)
    {
        var open = registry.RegisterKeyBinding("sample:charms", "key.keyboard.k", "sample:engine");
        open.Pressed += () => registry.Connection.SendPayload(new OpenCharms());

        registry.RegisterMenuScreen<CharmsView>(MenuIdentifier);
    }

    /// <summary>
    /// Grants the player an extra jump for each charm worn, or takes the grant back when none is.
    /// </summary>
    public static void Apply(IPlayer player)
    {
        var worn = player.GetAttachment(Worn) ?? [];
        var charms = worn.Count(item => item?.Is(Charm) == true);

        if (charms > 0)
            player.SetMovementAbilities(AbilitiesId, new MovementAbilities { ExtraJumps = charms });
        else
            player.RemoveMovementAbilities(AbilitiesId);
    }

    private static async ValueTask OpenAsync(OpenCharms payload, PayloadContext context)
    {
        if (context.Player is IPlayer player)
            await player.OpenInventoryAsync(new CharmMenu(Menu, player));
    }

    private static ValueTask JoinedAsync(PlayerJoinEventArgs args)
    {
        Apply(args.Player);

        return ValueTask.CompletedTask;
    }
}
