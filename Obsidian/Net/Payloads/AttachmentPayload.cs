using Obsidian.API.Data;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.Net.Payloads;

internal enum AttachmentTargetKind : byte
{
    Player,
    Entity,
    Chunk
}

internal readonly record struct AttachmentTarget(AttachmentTargetKind Kind, int X = 0, int Z = 0);

/// <summary>
/// Full replacement of one holder's synced attachments on an optional Obsidian channel. Names and framed values
/// allow missing/unloaded client types to be skipped without invoking their codecs or changing vanilla registries.
/// </summary>
internal static class AttachmentPayload
{
    internal const string Channel = "obsidian:attachments";
    internal const int MaxBytes = 1 << 20;
    internal const int MaxEntries = 1024;

    internal static byte[] Encode(AttachmentTarget target, NbtCompound saved)
    {
        var values = new List<(string Identifier, byte[] Data)>();
        foreach (var (identifier, tag) in saved)
        {
            var binding = PluginDataBindings.FindAttachment(identifier);
            using var call = binding?.TryEnter();
            if (call is null || binding?.WriteNetwork is not Action<INbtTag, INetStreamWriter> write)
                continue;

            using var value = new NetworkBuffer();
            write(tag, value);
            values.Add((identifier, value.AsSpan(0, value.Size).ToArray()));
        }

        if (values.Count > MaxEntries)
            throw new InvalidDataException("Too many synced attachments on one holder.");

        using var buffer = new NetworkBuffer();
        buffer.WriteByte((byte)target.Kind);
        if (target.Kind != AttachmentTargetKind.Player)
            buffer.WriteInt(target.X);

        if (target.Kind == AttachmentTargetKind.Chunk)
            buffer.WriteInt(target.Z);

        buffer.WriteVarInt(values.Count);
        foreach (var (identifier, data) in values)
        {
            buffer.WriteString(identifier);
            buffer.WriteVarInt(data.Length);
            buffer.WriteByteArray(data);
        }

        if (buffer.Size > MaxBytes)
            throw new InvalidDataException("Synced attachments exceed the payload limit.");

        return buffer.AsSpan(0, buffer.Size).ToArray();
    }

    internal static (AttachmentTarget Target, NbtCompound Data) Decode(NetworkBuffer buffer, bool client = true)
    {
        if (buffer.Size - buffer.Offset > MaxBytes)
            throw new InvalidDataException("Attachment payload is too large.");

        var kind = (AttachmentTargetKind)buffer.ReadByte();
        if (kind is not (AttachmentTargetKind.Player or AttachmentTargetKind.Entity or AttachmentTargetKind.Chunk))
            throw new InvalidDataException("Invalid attachment target.");

        var x = kind == AttachmentTargetKind.Player ? 0 : buffer.ReadInt();
        var z = kind == AttachmentTargetKind.Chunk ? buffer.ReadInt() : 0;
        var count = buffer.ReadVarInt();
        if (count < 0 || count > MaxEntries)
            throw new InvalidDataException("Invalid attachment count.");

        var saved = new NbtCompound(AttachmentStorage.NbtKey);
        for (var i = 0; i < count; i++)
        {
            var identifier = buffer.ReadString();
            var length = buffer.ReadVarInt();
            if (length < 0 || length > buffer.Size - buffer.Offset)
                throw new InvalidDataException("Invalid attachment value length.");

            using var value = new NetworkBuffer(length == 0 ? [] : buffer.ReadUInt8Array(length))
            {
                IdMaps = buffer.IdMaps
            };
            var binding = PluginDataBindings.FindAttachment(identifier, client);
            using var call = binding?.TryEnter();
            if (call is null || binding?.ReadNetwork is not Func<INetStreamReader, INbtTag> read)
                continue;

            var tag = read(value);
            if (value.Offset != value.Size)
                throw new InvalidDataException($"{identifier}'s codec didn't consume its attachment.");

            saved.Add(identifier, AttachmentStorage.CopyValue(identifier, tag));
        }

        if (buffer.Offset != buffer.Size)
            throw new InvalidDataException("Trailing attachment payload bytes.");

        return (new AttachmentTarget(kind, x, z), saved);
    }
}
