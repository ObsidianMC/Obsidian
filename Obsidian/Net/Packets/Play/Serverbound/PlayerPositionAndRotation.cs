﻿using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PlayerPositionAndRotation : IServerboundPacket
    {
        [Field(0), DataFormat(typeof(double))]
        public VectorF Position { get; private set; }

        [Field(1), DataFormat(typeof(float))]
        public Angle Yaw { get; private set; }

        [Field(2), DataFormat(typeof(float))]
        public Angle Pitch { get; private set; }

        [Field(3)]
        public bool OnGround { get; private set; }

        public int Id => 0x34;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(Position, Yaw, Pitch, OnGround);
        }
    }
}
