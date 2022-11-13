﻿using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class CustomSoundEffectPacket : IClientboundPacket
{
    [Field(0)]
    public string Name { get; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public SoundCategory Category { get; }

    [Field(2)]
    public SoundPosition Position { get; }

    [Field(3)]
    public float Volume { get; }

    [Field(4)]
    public float Pitch { get; }

    [Field(5)]
    public long Seed { get; set; } = 0;//TODO generate seed

    public int Id => 0x17;

    public CustomSoundEffectPacket(string name, SoundPosition position, SoundCategory category, float volume, float pitch)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty.", nameof(name));
        }

        Name = name;
        Position = position;
        Category = category;
        Volume = volume;
        Pitch = pitch;
    }
}
