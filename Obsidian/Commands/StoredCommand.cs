using Microsoft.Extensions.Logging;
using Obsidian.API.Commands;
using Obsidian.Nbt;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System.Globalization;
using System.Text;

namespace Obsidian.Commands;

/// <summary>
/// The command a command block or a command block minecart keeps, and what its last run did, like vanilla's
/// <c>BaseCommandBlock</c>: both run it through <see cref="PerformAsync"/> and save it with <see cref="Save"/>.
/// </summary>
/// <remarks>
/// A run changes it on its level's tick, and an edit under the level's tick lock; a command block saves it from any
/// thread (see <see cref="WorldData.CommandBlockEntity"/>).
/// </remarks>
internal sealed partial class StoredCommand
{
    /// <summary>
    /// The name a command block's commands go by without a custom name (vanilla's <c>DEFAULT_NAME</c>).
    /// </summary>
    public const string DefaultName = "@";

    // Vanilla's NO_LAST_EXECUTION.
    private const long NoLastExecution = -1;

    // Vanilla's easter egg: a block whose command is this says the other, and succeeds, without running anything.
    private const string Searge = "Searge";
    private const string SeargeOutput = "#itzlipofutzli";

    /// <summary>The command, which may be empty (vanilla's <c>Command</c>).</summary>
    public string Command { get; private set; } = string.Empty;

    /// <summary>
    /// How many times the command succeeded when it last ran (vanilla's <c>SuccessCount</c>), which comparators read
    /// from a command block.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Whether the command's output is kept and its successes told to operators (<c>TrackOutput</c>).
    /// </summary>
    public bool TrackOutput { get; set; } = true;

    /// <summary>What the command last said, after the time it said it (<c>LastOutput</c>), or null.</summary>
    public ChatMessage? LastOutput { get; set; }

    /// <summary>The block's custom name (<c>CustomName</c>), which its commands go by; null for "@".</summary>
    public ChatMessage? CustomName { get; set; }

    /// <summary>
    /// The game time the command last ran at (<c>LastExecution</c>), which keeps it from running twice in one tick,
    /// or -1.
    /// </summary>
    public long LastExecution { get; private set; } = NoLastExecution;

    /// <summary>Whether a run records <see cref="LastExecution"/> (<c>UpdateLastExecution</c>).</summary>
    public bool UpdateLastExecution { get; private set; } = true;

    /// <summary>The name the commands go by: the custom name's text, or "@".</summary>
    public string Name => this.CustomName is ChatMessage name ? PlainText(name) : DefaultName;

    /// <summary>
    /// Sets the command, which forgets how often the last one succeeded (vanilla's <c>setCommand</c>).
    /// </summary>
    public void SetCommand(string command)
    {
        this.Command = command;
        this.SuccessCount = 0;
    }

    /// <summary>
    /// Runs the command, like vanilla's <c>performCommand</c>: at most once a game tick, only while the level's
    /// <c>command_blocks_work</c> rule is on, at the game master permission level (2), going by <see cref="Name"/>.
    /// The command's output is kept and its successes told to operators as <see cref="TrackOutput"/> and the
    /// <c>send_command_feedback</c> and <c>command_block_output</c> rules say.
    /// </summary>
    /// <returns>False when the command already ran this game tick, which ends a chain; true otherwise.</returns>
    /// <remarks>
    /// Call on the level's tick. Commands that would wait for the gates (a <c>/save</c>) leave their work to the
    /// server's background work, as they do from any tick (see <see cref="Server.RunOutsideTicksAsync"/>).
    /// </remarks>
    public async ValueTask<bool> PerformAsync(CommandRun run)
    {
        var level = run.Level;
        var gameTime = ((WorldData.Redstone.IRedstoneLevel)level).GameTime;
        if (gameTime == this.LastExecution)
            return false;

        if (string.Equals(this.Command, Searge, StringComparison.OrdinalIgnoreCase))
        {
            this.LastOutput = ChatMessage.Simple(SeargeOutput);
            this.SuccessCount = 1;
            return true;
        }

        this.SuccessCount = 0;
        var rules = level.LevelData;
        if (rules.GetBooleanRule("command_blocks_work") && this.Command.Length > 0
            && level.OwningServer is Server server)
        {
            this.LastOutput = null;
            await this.RunAsync(server, run);
        }

        this.LastExecution = this.UpdateLastExecution ? gameTime : NoLastExecution;
        return true;
    }

    private async ValueTask RunAsync(Server server, CommandRun run)
    {
        var sender = new CommandBlockSender(this.Name);

        // Vanilla's performPrefixedCommand takes the command with or without its slash.
        var line = this.Command.StartsWith('/') ? this.Command : $"/{this.Command}";
        var context = new CommandContext(line, sender, null, server)
        {
            Executor = run.Executor,
            Origin = run.Origin,
            Level = run.Level,
            Rotation = run.Rotation
        };

        try
        {
            await server.CommandHandler.ProcessCommand(context);
        }
        catch (Exception exception)
        {
            Log.CommandFailed(run.Level.Logger, this.Command, exception);
            return;
        }

        // Vanilla counts each successful result; a command here reports one.
        if (context.Succeeded)
            this.SuccessCount = 1;

        // Without tracked output vanilla runs the command with no source to tell: nothing is kept or told.
        if (!this.TrackOutput)
            return;

        var rules = run.Level.LevelData;
        var feedback = rules.GetBooleanRule("send_command_feedback");
        foreach (var message in sender.Messages)
        {
            // A failure is always kept; a success only with feedback on, which operators are told of too.
            if (!context.Succeeded)
            {
                this.LastOutput = Timed(message);
                continue;
            }

            if (!feedback)
                continue;

            this.LastOutput = Timed(message);
            if (rules.GetBooleanRule("command_block_output"))
                await InformOperatorsAsync(server, sender.Name, message);
        }
    }

    // Vanilla's broadcastToAdmins: "[name: message]" in grey italics to every online operator.
    private static async ValueTask InformOperatorsAsync(Server server, string name, ChatMessage message)
    {
        var notice = new ChatMessage
        {
            Text = $"[{name}: {PlainText(message)}]",
            Color = HexColor.Gray,
            Italic = true
        };

        foreach (var player in server.OnlinePlayers.Values)
        {
            if (server.Operators.IsOperator(player))
                await player.SendMessageAsync(notice);
        }
    }

    // Vanilla's CloseableCommandBlockSource.sendSystemMessage: the message after the local time it was said.
    private static ChatMessage Timed(ChatMessage message)
    {
        var time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return ChatMessage.Simple($"[{time}] ").AddExtra(message);
    }

    /// <summary>
    /// A message's text and its extras' in order, without formatting: its styles, and the '§' codes some messages
    /// carry their colour in (a failure's red), which vanilla gives as a style instead.
    /// </summary>
    internal static string PlainText(ChatMessage message)
    {
        var builder = new StringBuilder();
        AppendPlainText(builder, message);
        return builder.ToString();
    }

    private static void AppendPlainText(StringBuilder builder, ChatMessage message)
    {
        var text = message.Text ?? string.Empty;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '§')
                index++;
            else
                builder.Append(text[index]);
        }

        foreach (var extra in message.GetExtras())
            AppendPlainText(builder, extra);
    }

    /// <summary>
    /// Writes the fields vanilla's <c>BaseCommandBlock.save</c> writes. <c>LastOutput</c> only while the output is
    /// tracked, and <c>LastExecution</c> only while it's recorded and set.
    /// </summary>
    /// <param name="withName">
    /// Whether to write <c>CustomName</c>: a minecart's custom name is its entity's, which it saves itself.
    /// </param>
    public void Save(NbtCompound tag, bool withName)
    {
        tag.Add(new NbtTag<string>("Command", this.Command));
        tag.Add(new NbtTag<int>("SuccessCount", this.SuccessCount));
        if (withName && this.CustomName is ChatMessage name)
            tag.Add(name.TextToNbt("CustomName"));

        tag.Add(new NbtTag<bool>("TrackOutput", this.TrackOutput));
        if (this.TrackOutput && this.LastOutput is ChatMessage output)
            tag.Add(output.TextToNbt("LastOutput"));

        tag.Add(new NbtTag<bool>("UpdateLastExecution", this.UpdateLastExecution));
        if (this.UpdateLastExecution && this.LastExecution != NoLastExecution)
            tag.Add(new NbtTag<long>("LastExecution", this.LastExecution));
    }

    /// <summary>
    /// Reads what <see cref="Save"/> wrote, with vanilla's defaults for missing fields (vanilla's <c>load</c>).
    /// </summary>
    /// <param name="withName">Whether to read <c>CustomName</c> (see <see cref="Save"/>).</param>
    public void Load(NbtCompound tag, bool withName)
    {
        this.Command = tag.TryGetTagValue<string>("Command", out var command) ? command : string.Empty;
        this.SuccessCount = tag.TryGetNumber<int>("SuccessCount", out var successes) ? successes : 0;
        if (withName)
            this.CustomName = tag.TryGetTag("CustomName", out var name) ? name.TextFromNbt() : null;

        this.TrackOutput = !tag.TryGetBool("TrackOutput", out var track) || track;
        this.LastOutput = this.TrackOutput && tag.TryGetTag("LastOutput", out var output) ? output.TextFromNbt() : null;
        this.UpdateLastExecution = !tag.TryGetBool("UpdateLastExecution", out var update) || update;
        var hasExecution = tag.TryGetNumber<long>("LastExecution", out var execution);
        this.LastExecution = this.UpdateLastExecution && hasExecution ? execution : NoLastExecution;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "A command block's command \"{Command}\" threw")]
        public static partial void CommandFailed(ILogger logger, string command, Exception exception);
    }
}

/// <summary>Where and as whom a <see cref="StoredCommand"/> runs.</summary>
/// <param name="Level">The level it runs in.</param>
/// <param name="Origin">Where it runs: a command block's centre, or a minecart's position.</param>
/// <param name="Rotation">The rotation local coordinates follow: a command block's facing, or a minecart's.</param>
/// <param name="Executor">The entity it runs as (a minecart), or null for a command block.</param>
internal readonly record struct CommandRun(
    AbstractLevel Level,
    VectorD Origin,
    (float Yaw, float Pitch) Rotation,
    IEntity? Executor);

/// <summary>
/// A command block's commands' sender: the game master permission level (2), the block's name, and what the command
/// says, which <see cref="StoredCommand"/> keeps or tells operators once it ran.
/// </summary>
/// <remarks>
/// Its issuer is the console's, which commands that only some issuers may run accept, but its permission level is
/// its own (see <see cref="IPermissionLevelSender"/>).
/// </remarks>
internal sealed class CommandBlockSender(string name) : ICommandSender, IPermissionLevelSender
{
    // Vanilla's LevelBasedPermissionSet.GAMEMASTER.
    private const int GameMasterLevel = 2;

    private readonly List<ChatMessage> messages = [];

    public CommandIssuers Issuer => CommandIssuers.Console;

    public IPlayer? Player => null;

    public int PermissionLevel => GameMasterLevel;

    /// <summary>The name commands show for the block: its custom name, or "@".</summary>
    public string Name => name;

    /// <summary>What the command said, in order.</summary>
    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (this.messages)
                return [.. this.messages];
        }
    }

    public Task SendMessageAsync(ChatMessage message)
    {
        lock (this.messages)
            this.messages.Add(message);

        return Task.CompletedTask;
    }

    public Task SendMessageAsync(ChatMessage message, Guid sender) => this.SendMessageAsync(message);

    public override string ToString() => name;
}
