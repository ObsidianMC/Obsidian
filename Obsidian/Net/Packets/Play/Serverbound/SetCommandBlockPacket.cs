using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// What an operator's command block editor sends (vanilla's <c>ServerboundSetCommandBlockPacket</c>): the block's
/// command, mode, and whether it's conditional, always active and keeps its output. Only operators in creative mode
/// may edit (see <see cref="CommandBlockMinecart.CanEdit"/>).
/// </summary>
public partial class SetCommandBlockPacket
{
    // The flags byte's bits, as vanilla writes them.
    private const int TrackOutputFlag = 1;
    private const int ConditionalFlag = 2;
    private const int AutomaticFlag = 4;

    // Vanilla's advMode.notAllowed, sent to a player who may not edit.
    private const string NotAllowed = "Must be an opped player in creative mode";

    public Vector Position { get; private set; }

    public string Command { get; private set; } = string.Empty;

    internal CommandBlockMode Mode { get; private set; }

    public bool TrackOutput { get; private set; }

    public bool Conditional { get; private set; }

    public bool Automatic { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadPosition();
        this.Command = reader.ReadString();
        this.Mode = (CommandBlockMode)reader.ReadVarInt();

        var flags = reader.ReadByte();
        this.TrackOutput = (flags & TrackOutputFlag) != 0;
        this.Conditional = (flags & ConditionalFlag) != 0;
        this.Automatic = (flags & AutomaticFlag) != 0;
    }

    // Vanilla's handleSetCommandBlock. Like a block use, it changes the block at once, under the level's tick lock,
    // so the tick it schedules runs on the level's next tick, as vanilla's does.
    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (!CommandBlockMinecart.CanEdit(player))
        {
            await player.SendMessageAsync(ChatMessage.Simple(NotAllowed));
            return;
        }

        if (!Enum.IsDefined(this.Mode) || player.Level is not AbstractLevel level || !this.Apply(level))
            return;

        if (this.Command.Length == 0)
            return;

        // Vanilla's advMode.setCommand.success, or .disabled while the command_blocks_work rule is off.
        var working = level.LevelData.GetBooleanRule("command_blocks_work");
        var message = working
            ? $"Command set: {this.Command}"
            : $"Command set: {this.Command}, but command blocks are still disabled";
        await player.SendMessageAsync(ChatMessage.Simple(message));
    }

    // Edits the command block at the position, if there is one there.
    private bool Apply(AbstractLevel level)
    {
        var position = this.Position;
        var updates = level.BlockUpdates;
        IRedstoneLevel redstone = level;

        lock (level.Fluids.TickLock)
        {
            if (updates.GetBlockEntity(position) is not CommandBlockEntity entity)
                return false;

            // The block of the new mode, facing as the old one did.
            var state = updates.GetBlock(position);
            var previousMode = CommandBlocks.ModeOf(state);
            var edited = CommandBlocks.BlockOf(this.Mode)
                .WithProperty("facing", state.GetProperty("facing")!)
                .WithProperty("conditional", this.Conditional);

            // Vanilla's flag 2: clients hear of it, neighbours don't. The block entity stays, as the three blocks
            // share its type.
            if (!edited.IsSameState(state))
                updates.Set(position, edited, BlockUpdateFlags.Clients);

            var command = entity.Command;
            command.SetCommand(this.Command);
            command.TrackOutput = this.TrackOutput;
            if (!this.TrackOutput)
                command.LastOutput = null;

            CommandBlocks.SetAutomatic(redstone, position, edited, entity, this.Automatic);
            if (previousMode != this.Mode)
                CommandBlocks.OnModeSwitch(redstone, position, edited, entity);

            entity.SetChanged();
        }

        return true;
    }
}
