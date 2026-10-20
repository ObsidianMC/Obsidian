namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Removes a score holder's score for one objective, or for every objective without one.
/// </summary>
public partial class ResetScorePacket
{
    public required string EntityName { get; init; }

    public string? ObjectiveName { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.EntityName);
        writer.WriteBoolean(this.ObjectiveName is not null);
        if (this.ObjectiveName is not null)
            writer.WriteString(this.ObjectiveName);
    }
}
