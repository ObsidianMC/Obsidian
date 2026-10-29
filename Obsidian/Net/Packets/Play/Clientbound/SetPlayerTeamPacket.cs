using Obsidian.Serialization.Attributes;

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

    [Field(9), Condition("Mode == TeamModeOption.CreateTeam || Mode == TeamModeOption.AddEntities || Mode == TeamModeOption.RemoveEntities")]
    public HashSet<string> Entities { get; set; } = [];

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.TeamName);
        writer.WriteByte((sbyte)this.Mode);

        if (this.Mode == TeamModeOption.UpdateTeam || this.Mode == TeamModeOption.CreateTeam)
        {
            writer.WriteChat(this.TeamDisplayName!);
            writer.WriteChat(this.TeamPrefix ?? "");
            writer.WriteChat(this.TeamSuffix ?? "");
            // Vanilla's team visibility/collision ids differ from the public API's enum order.
            writer.WriteVarInt(this.NameTagVisibility switch
            {
                NameTagVisibility.Never => 1,
                NameTagVisibility.HideForOtherTeams => 2,
                NameTagVisibility.HideForOwnTeam => 3,
                _ => 0
            });
            writer.WriteVarInt(this.CollisionRule switch
            {
                CollisionRule.Never => 1,
                CollisionRule.PushOtherTeams => 2,
                CollisionRule.PushOwnTeam => 3,
                _ => 0
            });
            var hasColor = (int)this.TeamColor is >= 0 and < 16;
            writer.WriteBoolean(hasColor);
            if (hasColor)
                writer.WriteVarInt((int)this.TeamColor);
            writer.WriteByte((sbyte)this.FriendlyFlags);
        }

        if (this.Mode is TeamModeOption.CreateTeam or TeamModeOption.AddEntities or TeamModeOption.RemoveEntities)
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
