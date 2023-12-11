﻿using Obsidian.Serialization.Attributes;
using System.Text.Json;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateTeamsPacket : IClientboundPacket
{
    [Field(0), FixedLength(16)]
    public string TeamName { get; set; }

    [Field(1), ActualType(typeof(sbyte))]
    public TeamModeOption Mode { get; set; }

    [Field(2), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public ChatMessage TeamDisplayName { get; set; }

    [Field(3), ActualType(typeof(sbyte)), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public TeamFriendlyFlags FriendlyFlags { get; set; }

    [Field(4), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public NameTagVisibility NameTagVisibility { get; set; }

    [Field(5), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public CollisionRule CollisionRule { get; set; }

    [Field(6), ActualType(typeof(int)), VarLength, Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public TeamColor TeamColor { get; set; }

    [Field(7), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public ChatMessage TeamPrefix { get; set; }

    [Field(8), Condition("Mode == TeamModeOption.UpdateTeam || Mode == TeamModeOption.CreateTeam")]
    public ChatMessage TeamSuffix { get; set; }

    [Field(9), Condition("Mode != TeamModeOption.RemoveTeam || Mode != TeamModeOption.UpdateTeam")]
    public HashSet<string> Entities { get; set; } = new();

    public int Id => 0x5E;

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteString(this.TeamName);
        packetStream.WriteByte((sbyte)this.Mode);

        if (this.Mode == TeamModeOption.UpdateTeam || this.Mode == TeamModeOption.CreateTeam)
        {
            packetStream.WriteChat(this.TeamDisplayName);
            packetStream.WriteByte((sbyte)this.FriendlyFlags);
            packetStream.WriteString(JsonNamingPolicy.CamelCase.ConvertName(this.NameTagVisibility.ToString()));
            packetStream.WriteString(JsonNamingPolicy.CamelCase.ConvertName(this.CollisionRule.ToString()));
            packetStream.WriteVarInt((int)this.TeamColor);
            packetStream.WriteChat(this.TeamPrefix ?? "");
            packetStream.WriteChat(this.TeamSuffix ?? "");
        }

        if (this.Mode != TeamModeOption.RemoveTeam || this.Mode != TeamModeOption.UpdateTeam)
        {
            packetStream.WriteVarInt(this.Entities.Count);
            foreach (var entity in this.Entities)
                packetStream.WriteString(entity);
        }

        stream.Lock.Wait();

        stream.WriteVarInt(this.Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);

        packetStream.Position = 0;
        packetStream.CopyTo(stream);

        stream.Lock.Release();
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
