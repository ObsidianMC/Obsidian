using Obsidian.Serialization.Attributes;
using System.Text.Json;

namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO clean this class up
public partial class SetPlayerTeamPacket
{
    [Field(0), FixedLength(16)]
    public string TeamName { get; set; }

    [Field(1), ActualType(typeof(sbyte))]
    public TeamModeOption Mode { get; set; }

    [Field(2), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public ChatMessage? TeamDisplayName { get; set; }

    [Field(3), ActualType(typeof(sbyte)), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public TeamFriendlyFlags FriendlyFlags { get; set; }

    [Field(4), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public NameTagVisibility NameTagVisibility { get; set; }

    [Field(5), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public CollisionRule CollisionRule { get; set; }

    [Field(6), ActualType(typeof(int)), VarLength, Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public TeamColor TeamColor { get; set; }

    [Field(7), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public ChatMessage? TeamPrefix { get; set; }

    [Field(8), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public ChatMessage? TeamSuffix { get; set; }

    [Field(9), Condition("Mode != TeamModeOption.RemoveTeam || Mode != TeamModeOption.UpdateTeam")]
    public HashSet<string> Entities { get; set; } = [];

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.TeamName);
        writer.WriteByte((sbyte)this.Mode);

        if (this.Mode == TeamModeOption.UpdateTeam || this.Mode == TeamModeOption.CreateTeam)
        {
            writer.WriteChat(this.TeamDisplayName!);
            writer.WriteByte((sbyte)this.FriendlyFlags);
            writer.WriteString(JsonNamingPolicy.CamelCase.ConvertName(this.NameTagVisibility.ToString()));
            writer.WriteString(JsonNamingPolicy.CamelCase.ConvertName(this.CollisionRule.ToString()));
            writer.WriteVarInt((int)this.TeamColor);
            writer.WriteChat(this.TeamPrefix ?? "");
            writer.WriteChat(this.TeamSuffix ?? "");
        }

        if (this.Mode != TeamModeOption.RemoveTeam || this.Mode != TeamModeOption.UpdateTeam)
        {
            writer.WriteVarInt(this.Entities.Count);
            foreach (var entity in this.Entities)
                writer.WriteString(entity);
        }
    }
}


[Flags]
public enum TeamFriendlyFlags : sbyte
{
    AllowFriendlyFire = 1,
    CanSeeInvisibleTeammates
}

public enum TeamModeOption : sbyte
{
    CreateTeam,
    RemoveTeam,
    UpdateTeam,
    AddEntities,
    RemoveEntities
}
