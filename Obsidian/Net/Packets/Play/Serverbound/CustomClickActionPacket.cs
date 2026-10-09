using Obsidian.API.Events;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using System.Buffers.Binary;
using System.IO;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class CustomClickActionPacket
{
    private const int MaximumPayloadBytes = 65536;
    private const int MaximumPayloadTags = 4096;
    private const int MaximumPayloadDepth = 32;
    public string ActionId { get; private set; } = "";
    public INbtTag? Payload { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        ActionId = reader.ReadString(32767);
        var bytes = reader.ReadByteArray(MaximumPayloadBytes);
        ValidatePayload(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        Payload = new NbtReader(stream).ReadNextTag(false);
        if (stream.Position != stream.Length) throw new InvalidDataException("Unread custom action payload data.");
    }

    public override async ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var args = new DialogActionEventArgs(player, server, ActionId, Payload);
        await server.EventDispatcher.ExecuteEventAsync(args);
        if (args.Handled) return;

        if (ActionId == Entities.Player.WelcomeContinueActionId)
            await player.ClearDialogAsync();
        else if (server is Server obsidian && player is Entities.Player connectedPlayer)
            await obsidian.HandleFeedbackAsync(connectedPlayer, ActionId, Payload);
    }

    // Bound nesting and declared counts before the general NBT reader can recurse or allocate.
    internal static void ValidatePayload(byte[] bytes)
    {
        var offset = 0;
        var tagsRemaining = MaximumPayloadTags;
        byte Byte()
        {
            if (offset >= bytes.Length) throw new InvalidDataException("Truncated custom action payload.");
            return bytes[offset++];
        }
        void Skip(int count)
        {
            if (count < 0 || count > bytes.Length - offset) throw new InvalidDataException("Invalid NBT length.");
            offset += count;
        }
        int Count()
        {
            var start = offset;
            Skip(4);
            var count = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(start, 4));
            if (count < 0 || count > MaximumPayloadBytes) throw new InvalidDataException("Invalid NBT count.");
            return count;
        }
        void String()
        {
            var start = offset;
            Skip(2);
            Skip(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(start, 2)));
        }
        void Tag(byte type, int depth)
        {
            if (depth > MaximumPayloadDepth || --tagsRemaining < 0) throw new InvalidDataException("Custom action payload is too complex.");
            switch (type)
            {
                case 0: break;
                case 1: Skip(1); break;
                case 2: Skip(2); break;
                case 3: case 5: Skip(4); break;
                case 4: case 6: Skip(8); break;
                case 7: Skip(Count()); break;
                case 8: String(); break;
                case 9:
                    var element = Byte();
                    var count = Count();
                    if (element > 12 || (element == 0 && count != 0)) throw new InvalidDataException("Invalid NBT list type.");
                    for (var i = 0; i < count; i++) Tag(element, depth + 1);
                    break;
                case 10:
                    byte child;
                    while ((child = Byte()) != 0) { String(); Tag(child, depth + 1); }
                    break;
                case 11: Skip(checked(Count() * 4)); break;
                case 12: Skip(checked(Count() * 8)); break;
                default: throw new InvalidDataException("Invalid NBT tag type.");
            }
        }
        Tag(Byte(), 0);
        if (offset != bytes.Length) throw new InvalidDataException("Trailing custom action payload data.");
    }
}
