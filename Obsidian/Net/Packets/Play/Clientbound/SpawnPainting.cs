using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SpawnPainting : IClientboundPacket
{
    [Field(0), VarLength]
    private const int EntityId = 9; // Source: https://minecraft.gamepedia.com/Java_Edition_data_values/Pre-flattening/Entity_IDs

    [Field(1)]
    public Guid UUID { get; }

    [Field(2), VarLength]
    public int Motive { get; }

    [Field(3)]
    public Vector Position { get; }

    [Field(4), ActualType(typeof(byte))]
    public PaintingDirection Direction { get; }

    public int Id => 0x03;

    public SpawnPainting(Guid uuid, int motive, Vector position, PaintingDirection direction)
    {
        UUID = uuid;
        Motive = motive;
        Position = position;
        Direction = direction;
    }
}
