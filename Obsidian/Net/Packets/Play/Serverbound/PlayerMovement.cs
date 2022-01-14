﻿using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerMovement : IServerboundPacket
{
    [Field(0)]
    public bool OnGround { get; set; }

    public int Id => 0x14;

    public ValueTask HandleAsync(Server server, Player player)
    {
        //var fallDistance = player.LastPosition.Z - player.Position.Z;
        //await player.DamageAsync(null, MathF.Max(0, fallDistance - 3f));
        return ValueTask.CompletedTask;
    }
}
