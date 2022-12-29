using Obsidian.Net;

namespace Obsidian.Utilities.Mojang;

public class MojangUser
{
    public string Id { get; set; }

    public string Name { get; set; }

    public bool Legacy { get; set; }

    public bool Demo { get; set; }

    public List<SkinProperty> Properties { get; set; }
}

public sealed class SkinProperty
{
    public string Name { get; set; }

    public string Value { get; set; }
    public string? Signature { get; set; }

    public async Task WriteAsync(MinecraftStream stream)
    {
        var isSigned = this.Signature != null;
        await stream.WriteStringAsync(this.Name, 32767);
        await stream.WriteStringAsync(this.Value, 32767);
        await stream.WriteBooleanAsync(isSigned);
        if (isSigned)
            await stream.WriteStringAsync(this.Signature, 32767);
    }

    public void Write(MinecraftStream stream)
    {
        stream.WriteString(Name);
        stream.WriteString(Value);
        stream.WriteBoolean(Signature is not null);
        if (Signature is not null)
            stream.WriteString(Signature);
    }
}
