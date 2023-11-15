﻿using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateObjectivesPacket : IClientboundPacket
{
    [Field(0)]
    public required string ObjectiveName { get; init; }

    [Field(1), ActualType(typeof(sbyte))]
    public required ScoreboardMode Mode { get; init; }

    [Field(2), ActualType(typeof(ChatMessage)), Condition(nameof(ShouldWriteValue))]
    public ChatMessage? Value { get; init; }

    [Field(3), VarLength, ActualType(typeof(int)), Condition(nameof(ShouldWriteValue))]
    public DisplayType Type { get; init; }

    public int Id => 0x5A;

    private bool ShouldWriteValue => Mode is ScoreboardMode.Create or ScoreboardMode.Update;
}

public enum ScoreboardMode : sbyte
{
    Create,
    Remove,
    Update
}
