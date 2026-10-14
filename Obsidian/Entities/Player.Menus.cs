using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Events;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Net.WindowProperties;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Entities;

// Plugins' menus open for the player: opening one with its extra data, sending its data slots, keeping it open only
// while it's valid, and its buttons.
public partial class Player
{
    // How much farther than their block reach a player may be from a menu's block entity: vanilla's
    // Container.DEFAULT_DISTANCE_BUFFER.
    private const double MenuReachBuffer = 4.0;

    // Taken to send the open menu's data slots, as it opens and on the player's ticks, so that the values are sent in
    // the order they were read.
    private readonly SemaphoreSlim menuSyncGate = new(1, 1);

    // Coordinates changing the open container with installing or clearing its last menu snapshot. Packet sends
    // hold only menuSyncGate, so a close from a packet handler doesn't wait for its own send to finish.
    private readonly object menuStateGate = new();

    // What the player was last sent of their open menu (see MenuSync), changed under menuStateGate.
    private MenuSync? menuSync;

    /// <summary>
    /// Whether <paramref name="menu"/> stays open for <paramref name="player"/>, like vanilla's <c>stillValid</c>: its
    /// block entity, if it has one, is still in the player's level, and its block within their block reach plus 4
    /// blocks of their eyes; and the menu says so (see <see cref="MenuContainer.StillValid"/>). Call it inside a call
    /// into the menu's plugin.
    /// </summary>
    internal static bool IsMenuValid(IPlayer player, MenuContainer menu)
    {
        if (menu.BlockEntity is BlockEntity blockEntity && !IsWithinReach(player, blockEntity))
            return false;

        return menu.StillValid(player);
    }

    /// <summary>
    /// The packet that opens <paramref name="menu"/> in window <paramref name="containerId"/>: for a NeoForge client,
    /// NeoForge's <c>neoforge:advanced_open_screen</c>, with the extra data the menu writes (see
    /// <see cref="MenuContainer.WriteOpenData"/>); for other clients, vanilla's open screen packet, which carries
    /// none.
    /// </summary>
    internal IClientboundPacket OpenMenuPacket(MenuContainer menu, byte containerId)
    {
        if (this.Client is not Client { Channels: ClientChannels channels }
            || !channels.HasNeoForgePlayChannel(ChannelPayloads.AdvancedOpenScreen))
        {
            return new OpenScreenPacket(menu, containerId);
        }

        using var extra = new NetworkBuffer();
        if (!menu.TryGetOwner(out var owner))
            return new OpenScreenPacket(menu, containerId);

        var plugin = owner as PluginContainer;

        using (var call = plugin?.TryEnter())
        {
            if (plugin is null || call is not null)
                menu.WriteOpenData(this, extra);
        }

        // NeoForge's AdvancedOpenScreenPayload: the window, the menu type's id, the title, then the extra data as a
        // byte array with its length.
        using var payload = new NetworkBuffer();
        payload.WriteVarInt(containerId);
        payload.WriteVarInt(menu.Menu.Id);
        payload.WriteChat(menu.Title);
        payload.WriteVarInt(extra.Size);
        payload.WriteByteArray(extra.AsSpan(0, extra.Size).ToArray());

        return ClientChannels.CreatePacket(
            NetworkProtocol.Play,
            ChannelPayloads.AdvancedOpenScreen,
            payload.AsSpan(0, payload.Size).ToArray());
    }

    /// <summary>
    /// Sends the player a menu that just opened in window <paramref name="containerId"/>: all its data slots, like
    /// vanilla's menus as they open. What its item access slots show is remembered, for the ticks to come.
    /// </summary>
    internal async ValueTask SendMenuOpenedAsync(MenuContainer menu, int containerId)
    {
        if (this.ReadMenu(menu, containerId) is MenuSync read)
            await this.SyncMenuAsync(read, all: true);
    }

    /// <summary>
    /// Keeps the player's open menu up to date, once a tick, like vanilla's <c>broadcastChanges</c>: it closes once
    /// it's no longer valid (see <see cref="IsMenuValid"/>); otherwise the data slots that changed are sent, and the
    /// whole window when an item access slot changed, which nothing else tells the server.
    /// </summary>
    internal async ValueTask TickMenuAsync()
    {
        if (this.OpenedContainer is not MenuContainer menu)
            return;

        var containerId = this.CurrentContainerId;
        if (!menu.TryGetOwner(out var owner))
            return;

        var plugin = owner as PluginContainer;
        bool valid;

        using (var call = plugin?.TryEnter())
        {
            // An unloading plugin's menus are closing already.
            if (plugin is not null && call is null)
                return;

            valid = IsMenuValid(this, menu);
        }

        if (!valid)
        {
            if (ReferenceEquals(this.OpenedContainer, menu) && this.CurrentContainerId == containerId)
                await this.CloseContainerAsync();

            return;
        }

        if (this.ReadMenu(menu, containerId) is MenuSync read && await this.SyncMenuAsync(read, all: false))
            await MainEventHandler.RefreshWindowAsync(this, menu, stateId: 0);
    }

    /// <summary>
    /// Handles the player clicking a button of their open menu's screen, like vanilla's
    /// <c>handleContainerButtonClick</c>: the menu handles it (see <see cref="MenuContainer.ClickButton"/>) inside its
    /// window's lock, like a click, and its viewers are sent its slots if it says they changed.
    /// </summary>
    internal async ValueTask ClickMenuButtonAsync(int containerId, int button)
    {
        if (containerId != this.CurrentContainerId
            || this.OpenedContainer is not MenuContainer menu
            || this.GameMode == GameMode.Spectator)
        {
            return;
        }

        bool changed;

        using (await BaseContainer.LockWindowAsync(menu, this.Inventory))
        {
            // Like a click, refused while the player is dead.
            if (!ReferenceEquals(this.OpenedContainer, menu)
                || this.CurrentContainerId != containerId
                || menu.ShowsRetired
                || !this.Alive)
            {
                return;
            }

            using var click = MainEventHandler.MenuClick.Start(this, menu)!;
            if (click.Refused)
                return;

            try
            {
                changed = menu.ClickButton(this, button);

                // A button whose items the access no longer held is undone; the window is sent again either way.
                changed |= !click.Commit();
            }
            catch
            {
                click.Undo();
                throw;
            }
        }

        if (changed)
            await MainEventHandler.RefreshViewersAsync(menu);
    }

    // Whether the player's client was sent the menu type with its registries. A vanilla client never is, and a
    // client configured before a plugin registered the type learns it only once it goes through configuration again.
    private bool KnowsMenu(MenuDefinition menu) =>
        this.Client is not Client client
        || !client.AwaitingReconfiguration
            && menu.Id < MenuTypesRegistry.VanillaIdentifiers.Count + client.ConfiguredPluginMenus;

    // The menu's data slot values and item access slots' stacks, read inside a call into its plugin, without the
    // menu's locks; null when the plugin is unloading.
    private MenuSync? ReadMenu(MenuContainer menu, int containerId)
    {
        if (!ReferenceEquals(this.OpenedContainer, menu)
            || this.CurrentContainerId != containerId
            || !menu.TryGetOwner(out var owner))
        {
            return null;
        }

        var plugin = owner as PluginContainer;

        using var call = plugin?.TryEnter();
        if (plugin is not null && call is null)
            return null;

        short[] data = [.. Enumerable.Range(0, menu.DataSlots).Select(menu.GetData)];
        ItemStack?[] items =
        [
            .. menu.Slots.Select(slot => slot.Items is IItemAccess access ? access.GetItem(slot.Index) : null)
        ];

        return new MenuSync(menu, containerId, data, items);
    }

    /// <summary>
    /// Sends the data slots of <paramref name="read"/> that the player wasn't sent yet, or all of them, while its menu
    /// is still open under the same window.
    /// </summary>
    /// <returns>Whether its item access slots changed since the last read, so that the window is due.</returns>
    private async ValueTask<bool> SyncMenuAsync(MenuSync read, bool all)
    {
        await this.menuSyncGate.WaitAsync();
        try
        {
            MenuSync? last;
            lock (this.menuStateGate)
            {
                if (!ReferenceEquals(this.OpenedContainer, read.Menu) || this.CurrentContainerId != read.ContainerId)
                    return false;

                last = !all && this.menuSync is MenuSync sent && sent.Shows(read) ? sent : null;
                this.menuSync = read;
            }

            for (var index = 0; index < read.Data.Length; index++)
            {
                if (last is not null && last.Data[index] == read.Data[index])
                    continue;

                await this.Client.QueuePacketAsync(new ContainerSetDataPacket
                {
                    ContainerId = read.ContainerId,
                    WindowProperty = new MenuDataProperty((short)index, read.Data[index])
                });
            }

            return last is not null && !read.Items.SequenceEqual(last.Items, ShownStackComparer.Instance);
        }
        finally
        {
            this.menuSyncGate.Release();
        }
    }

    // Vanilla's Container.stillValidBlockEntity: the block entity is still there, and the block's box within the
    // player's block reach plus the buffer of their eyes.
    private static bool IsWithinReach(IPlayer player, BlockEntity blockEntity)
    {
        if (blockEntity.IsRemoved || !ReferenceEquals(blockEntity.Level, player.Level))
            return false;

        var range = (player is Player concrete ? concrete.BlockInteractionRange : 4.5) + MenuReachBuffer;
        var eye = EyePosition(player);
        var block = (VectorD)blockEntity.BlockPosition;
        var x = eye.X - Math.Clamp(eye.X, block.X, block.X + 1);
        var y = eye.Y - Math.Clamp(eye.Y, block.Y, block.Y + 1);
        var z = eye.Z - Math.Clamp(eye.Z, block.Z, block.Z + 1);

        return x * x + y * y + z * z < range * range;
    }

    // What a menu open in a window showed: its data slot values and its item access slots' stacks.
    private sealed record MenuSync(MenuContainer Menu, int ContainerId, short[] Data, ItemStack?[] Items)
    {
        // Whether it's the same menu in the same window as other.
        public bool Shows(MenuSync other) =>
            ReferenceEquals(this.Menu, other.Menu) && this.ContainerId == other.ContainerId;
    }

    // A data slot of a plugin's menu, as vanilla's ContainerSetData sends it.
    private sealed record MenuDataProperty(short Property, short Value) : IWindowProperty;

    // Compares the stacks a window shows: the same item and components, and the same count.
    private sealed class ShownStackComparer : IEqualityComparer<ItemStack?>
    {
        public static ShownStackComparer Instance { get; } = new();

        public bool Equals(ItemStack? x, ItemStack? y) =>
            x.IsNullOrAir() ? y.IsNullOrAir() : !y.IsNullOrAir() && x.Equals(y) && x.Count == y.Count;

        public int GetHashCode(ItemStack? obj) => obj?.Count ?? 0;
    }
}
