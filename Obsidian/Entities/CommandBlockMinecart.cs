using Obsidian.API.Inventory;
using Obsidian.API.Commands;
using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with a command block, like vanilla's <c>MinecartCommandBlock</c>: on a powered activator rail it runs its
/// command, at most every four ticks, from where it is. Operators in creative set the command through its screen.
/// Breaking it drops a plain minecart.
/// </summary>
/// <remarks>
/// The command runs through the same <see cref="StoredCommand"/> as a command block's, at permission level 2, with the
/// cart as the executor, from where it is.
/// </remarks>
[MinecraftEntity("minecraft:command_block_minecart")]
public sealed partial class CommandBlockMinecart : AbstractMinecart
{
    // Vanilla's MinecartCommandBlock.ACTIVATION_DELAY.
    private const int ActivationDelay = 4;

    private int age;
    private int lastActivated = -ActivationDelay;

    public CommandBlockMinecart() => this.Type = EntityType.CommandBlockMinecart;

    /// <summary>The command it runs and what its last run did, which a command block keeps too.</summary>
    internal StoredCommand Stored { get; } = new();

    /// <summary>The command it runs (vanilla's <c>Command</c>).</summary>
    public string Command
    {
        get => this.Stored.Command;
        set => this.Stored.SetCommand(value);
    }

    /// <summary>How many times the command last succeeded (vanilla's <c>SuccessCount</c>).</summary>
    public int SuccessCount => this.Stored.SuccessCount;

    /// <summary>Whether it keeps the command's last output (vanilla's <c>TrackOutput</c>).</summary>
    public bool TrackOutput
    {
        get => this.Stored.TrackOutput;
        set => this.Stored.TrackOutput = value;
    }

    /// <summary>The command's last output, if it keeps it (vanilla's <c>LastOutput</c>).</summary>
    public ChatMessage? LastOutput => this.Stored.LastOutput;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.Minecart);

    internal override Item PickItem => ItemsRegistry.Get(ItemIds.CommandBlockMinecart);

    // Vanilla's interact: operators in creative mode open the command screen, which their client draws itself.
    public override ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand) =>
        ValueTask.FromResult(CanEdit(player));

    // Vanilla's Commands.LEVEL_GAMEMASTERS.
    private const int GameMasterLevel = 2;

    /// <summary>
    /// Vanilla's <c>canUseGameMasterBlocks</c>: a creative player with the game master permission level (2), which
    /// vanilla also asks of game master commands like <c>/locate</c>.
    /// </summary>
    internal static bool CanEdit(IPlayer player) =>
        player.GameMode == GameMode.Creative
        && player.Server.Operators is OperatorList operators
        && operators.GetPermissionLevel(player) >= GameMasterLevel;

    /// <summary>
    /// Sets the command and whether its output is kept, as an operator's command screen sends them.
    /// </summary>
    internal void Edit(string command, bool trackOutput)
    {
        this.Stored.SetCommand(command);
        this.Stored.TrackOutput = trackOutput;
        if (!trackOutput)
            this.Stored.LastOutput = null;

        this.DataChanged = true;
    }

    private protected override ValueTask AfterMoveAsync()
    {
        this.age++;
        return default;
    }

    // Vanilla's activateMinecart: a powered activator rail runs the command, at most every four ticks.
    private protected override async ValueTask ActivateAsync(Vector rail, bool powered)
    {
        if (!powered || this.age - this.lastActivated < ActivationDelay)
            return;

        await this.PerformCommandAsync();
        this.lastActivated = this.age;
    }

    // Vanilla's MinecartCommandBase: the command runs as the cart, from where it is, turned as it is.
    private async ValueTask PerformCommandAsync()
    {
        if (this.Level is not WorldData.AbstractLevel level)
            return;

        var run = new CommandRun(level, this.Position, (this.Yaw, this.Pitch), Executor: this);
        await this.Stored.PerformAsync(run);
        this.DataChanged = true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(13, EntityMetadataType.String);
        writer.WriteString(this.Command);

        writer.WriteEntityMetadataType(14, EntityMetadataType.TextComponent);
        writer.WriteChat(this.LastOutput ?? ChatMessage.Simple(string.Empty));

        this.MetadataIndex = 15;
    }

    // The cart's custom name is the entity's, which it saves itself.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        this.Stored.Save(tag, withName: false);
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.Stored.Load(tag, withName: false);
    }
}
