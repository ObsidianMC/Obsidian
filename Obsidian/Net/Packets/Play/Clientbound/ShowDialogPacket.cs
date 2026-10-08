using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.Nbt;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ShowDialogPacket
{
    public required DialogElement Dialog { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        // Holder<Dialog>: zero denotes an inline definition, positive values are registry id + 1.
        writer.WriteVarInt(0);
        using var nbt = new RawNbtWriter(true);
        Dialog.Write(nbt);
        nbt.EndCompound();
        nbt.TryFinish();
        writer.WriteByteArray(nbt.Data);
    }
}
