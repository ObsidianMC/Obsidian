﻿using Obsidian.Net;

namespace Obsidian.Utilities.Mojang;

public class MojangUser
{
    public string Id { get; set; }

    public string Name { get; set; }

    public bool Legacy { get; set; }

    public bool Demo { get; set; }

    public List<SkinProperties> Properties { get; set; }
}

public class SkinProperties
{
    public string Name { get; set; }

    public string Value { get; set; }
    public string Signature { get; set; }

    public async Task<byte[]> ToArrayAsync()
    {
        var isSigned = Signature != null;
        using var stream = new MinecraftStream();
        await stream.WriteStringAsync(Name, 32767);
        await stream.WriteStringAsync(Value, 32767);
        await stream.WriteBooleanAsync(isSigned);
        if (isSigned)
            await stream.WriteStringAsync(Signature, 32767);

        return stream.ToArray();
    }

    public byte[] ToArray()
    {
        using var stream = new MinecraftStream();
        stream.WriteString(Name);
        stream.WriteString(Value);
        stream.WriteBoolean(Signature is not null);
        if (Signature is not null)
            stream.WriteString(Signature);

        return stream.ToArray();
    }
}
