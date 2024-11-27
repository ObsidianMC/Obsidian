namespace Obsidian.Net.Actions.PlayerInfo;

public class AddPlayerInfoAction : InfoAction
{
    public required string Name { get; set; }

    public List<SkinProperty> Properties { get; set; } = [];

    public override PlayerInfoAction Type => PlayerInfoAction.AddPlayer;

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteString(Name, 16);
        writer.WriteVarInt(Properties.Count);

        foreach (var property in Properties)
            writer.WriteSkinProperty(property);
    }
}
