﻿using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class CraftRecipeRequest : IServerboundPacket
{
    [Field(0)]
    public sbyte WindowId { get; private set; }

    [Field(1)]
    public string RecipeId { get; private set; }

    [Field(2)]
    public bool MakeAll { get; private set; }

    public int Id => 0x18;

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await player.client.QueuePacketAsync(new CraftRecipeResponse(WindowId, RecipeId));
    }
}
