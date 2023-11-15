namespace Obsidian.Net.Actions.PlayerInfo;

public class AddPlayerInfoAction : InfoAction
{
    public string Name { get; set; }

    public List<SkinProperty> Properties { get; set; } = [];

    public override PlayerInfoAction Type => PlayerInfoAction.AddPlayer;

    public async override Task WriteAsync(MinecraftStream stream)
    {
        await stream.WriteStringAsync(this.Name, 16);

        await stream.WriteVarIntAsync(this.Properties.Count);

        foreach (var property in this.Properties)
            await stream.WriteSkinPropertyAsync(property);
    }

    public override void Write(MinecraftStream stream)
    {
        stream.WriteString(Name, 16);
        stream.WriteVarInt(Properties.Count);

        foreach (var property in Properties)
            stream.WriteSkinProperty(property);
    }
}
