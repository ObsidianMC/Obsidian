using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public sealed partial class Player
{
    public ValueTask ShowDialogAsync(DialogElement dialog)
    {
        dialog.Validate();
        return Client.QueuePacketAsync(new ShowDialogPacket { Dialog = dialog });
    }

    public ValueTask ClearDialogAsync() => Client.QueuePacketAsync(new ClearDialogPacket());
}
