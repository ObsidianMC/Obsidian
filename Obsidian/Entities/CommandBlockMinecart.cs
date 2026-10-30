using Obsidian.API.Inventory;
using Obsidian.API.Commands;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with a command block, like vanilla's <c>MinecartCommandBlock</c>: on a powered activator rail it runs its
/// command, at most every four ticks, from where it is. Operators in creative set the command through its screen.
/// Breaking it drops a plain minecart.
/// </summary>
/// <remarks>
/// The command runs as the console would, with the cart as the executor; vanilla runs it at permission level 2.
/// </remarks>
[MinecraftEntity("minecraft:command_block_minecart")]
public sealed partial class CommandBlockMinecart : AbstractMinecart
{
    // Vanilla's MinecartCommandBlock.ACTIVATION_DELAY.
    private const int ActivationDelay = 4;

    private int age;
    private int lastActivated = -ActivationDelay;
    private ChatMessage? lastOutput;

    public CommandBlockMinecart() => this.Type = EntityType.CommandBlockMinecart;

    /// <summary>The command it runs (vanilla's <c>Command</c>).</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>How many times the command last succeeded (vanilla's <c>SuccessCount</c>).</summary>
    public int SuccessCount { get; private set; }

    /// <summary>Whether it keeps the command's last output (vanilla's <c>TrackOutput</c>).</summary>
    public bool TrackOutput { get; set; } = true;

    /// <summary>The command's last output, if it keeps it (vanilla's <c>LastOutput</c>).</summary>
    public ChatMessage? LastOutput
    {
        get => this.lastOutput;
        private set
        {
            this.lastOutput = value;
            this.DataChanged = true;
        }
    }

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.Minecart);

    internal override Item PickItem => ItemsRegistry.Get(ItemIds.CommandBlockMinecart);

    // Vanilla's interact: operators in creative mode open the command screen, which their client draws itself.
    public override ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand) =>
        ValueTask.FromResult(CanEdit(player));

    /// <summary>
    /// Vanilla's <c>canUseGameMasterBlocks</c>: a creative operator.
    /// </summary>
    internal static bool CanEdit(IPlayer player) =>
        player.GameMode == GameMode.Creative && player.Server.Operators.IsOperator(player);

    /// <summary>
    /// Sets the command and whether its output is kept, as an operator's command screen sends them.
    /// </summary>
    internal void Edit(string command, bool trackOutput)
    {
        this.Command = command;
        this.TrackOutput = trackOutput;
        if (!trackOutput)
            this.LastOutput = null;

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

    // Vanilla's BaseCommandBlock.performCommand.
    private async ValueTask PerformCommandAsync()
    {
        this.SuccessCount = 0;
        if (string.IsNullOrWhiteSpace(this.Command)
            || this.Level.Players.Values.FirstOrDefault()?.Server is not IServer server)
        {
            return;
        }

        var sender = new MinecartCommandSender();
        var line = this.Command.StartsWith('/') ? this.Command : $"/{this.Command}";
        var context = new CommandContext(line, sender, null, server)
        {
            Executor = this,
            Origin = this.Position,
            Level = this.Level
        };

        try
        {
            await server.CommandHandler.ProcessCommand(context);
            this.SuccessCount = 1;
        }
        catch (Exception)
        {
            this.SuccessCount = 0;
        }

        if (this.TrackOutput && sender.Output is ChatMessage output)
            this.LastOutput = output;
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

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<string>("Command", this.Command));
        tag.Set(new NbtTag<int>("SuccessCount", this.SuccessCount));
        tag.Set(new NbtTag<bool>("TrackOutput", this.TrackOutput));
        tag.SetOrRemove("LastOutput", this.TrackOutput ? this.LastOutput?.ToNbt("LastOutput") : null);
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.Command = tag.TryGetTagValue<string>("Command", out var command) ? command : string.Empty;
        this.SuccessCount = tag.TryGetNumber<int>("SuccessCount", out var successes) ? successes : 0;
        this.TrackOutput = !tag.TryGetBool("TrackOutput", out var track) || track;
        this.lastOutput = this.TrackOutput && tag.TryGetTag("LastOutput", out var output) ? output.TextFromNbt() : null;
    }

    // Collects what the command says, the last line of which the cart keeps.
    private sealed class MinecartCommandSender : ICommandSender
    {
        public CommandIssuers Issuer => CommandIssuers.Console;

        public IPlayer? Player => null;

        public ChatMessage? Output { get; private set; }

        public Task SendMessageAsync(ChatMessage message)
        {
            this.Output = message;
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(ChatMessage message, Guid sender) => this.SendMessageAsync(message);
    }
}
