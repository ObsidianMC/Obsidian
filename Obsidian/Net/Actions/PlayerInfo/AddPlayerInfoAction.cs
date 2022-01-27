using Obsidian.Utilities.Mojang;

namespace Obsidian.Net.Actions.PlayerInfo;

public class AddPlayerInfoAction : InfoAction
{
    public string Name { get; set; }

    public List<SkinProperties> Properties { get; set; } = new List<SkinProperties>();

    public int Gamemode { get; set; }

    public int Ping { get; set; }

    public ChatMessage DisplayName { get; set; } = null;

    public bool HasDisplayName => DisplayName != null;

    public async override Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteStringAsync(Name, 16);

        await stream.WriteVarIntAsync(Properties.Count);

        foreach (var props in Properties)
            await stream.WriteAsync(await props.ToArrayAsync());


        await stream.WriteVarIntAsync(Gamemode);

        await stream.WriteVarIntAsync(Ping);

        await stream.WriteBooleanAsync(HasDisplayName);

        if (HasDisplayName)
            await stream.WriteChatAsync(DisplayName);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteString(Name);
        stream.WriteVarInt(Properties.Count);

        foreach (var properties in Properties)
            stream.Write(properties.ToArray());

        stream.WriteVarInt(Gamemode);
        stream.WriteVarInt(Ping);
        stream.WriteBoolean(HasDisplayName);
        if (HasDisplayName)
            stream.WriteChat(DisplayName);
    }
}
