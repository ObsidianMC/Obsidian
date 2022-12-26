using Obsidian.Utilities.Mojang;

namespace Obsidian.Net.Actions.PlayerInfo;

public class AddPlayerInfoAction : InfoAction
{
    public string Name { get; set; }

    public List<SkinProperty> Properties { get; set; } = new List<SkinProperty>();

    public override PlayerInfoAction Type => PlayerInfoAction.AddPlayer;

    public async override Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteStringAsync(this.Name, 16);

        await stream.WriteVarIntAsync(this.Properties.Count);

        foreach (var props in this.Properties)
            await stream.WriteAsync(await props.ToArrayAsync());
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteString(Name);
        stream.WriteVarInt(Properties.Count);

        foreach (var properties in Properties)
            stream.Write(properties.ToArray());
    }
}
