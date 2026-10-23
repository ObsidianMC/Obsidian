using Obsidian.API.Commands;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// Sent while a player types an argument whose command node asks the server for suggestions. <see cref="Command"/>
/// holds the chat or command block input up to the cursor, and the reply must echo <see cref="IdValue"/>.
/// </summary>
public partial class CommandSuggestionPacket
{
    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var context = new CommandContext(this.Command, new CommandSender(CommandIssuers.Client, player), player, server);

        var completion = await server.CommandHandler.CompleteAsync(context);

        await player.QueuePacketAsync(new CommandSuggestionsPacket
        {
            IdValue = this.IdValue,
            Start = completion.Start,
            Length = completion.Length,
            Suggestions = [.. completion.Suggestions.Select(x => new CommandSuggestionsEntry { Text = x.Text, Tooltip = x.Tooltip })]
        });
    }
}
