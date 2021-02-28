using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API._Enums;
using Obsidian.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public class InteractEntity : IPacket
    {
        public int EntityId { get; set; }

        public InteractionType Type { get; set; }

        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float TargetZ { get; set; }

        public Hand Hand { get; set; }

        public bool Sneaking { get; set; }

        public int Id => 0x0E;

        public Task HandleAsync(Server server, Player player)
        {
            var entity = player.GetEntitiesNear(6).FirstOrDefault(x => x.EntityId == this.EntityId);

            server.Logger.LogInformation("Interaction Type: {0} & Entity ID: {1}", this.Type.ToString(), this.EntityId);

            server.Logger.LogInformation("Entity Type: {0}", entity?.Type);

            return Task.CompletedTask;
        }

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.EntityId = await stream.ReadVarIntAsync();
            this.Type = (InteractionType)await stream.ReadVarIntAsync();

            if(this.Type == InteractionType.InteractAt)
            {
                this.TargetX = await stream.ReadFloatAsync();
                this.TargetY = await stream.ReadFloatAsync();
                this.TargetZ = await stream.ReadFloatAsync();

                this.Hand = (Hand)await stream.ReadVarIntAsync();
            }

            this.Sneaking = await stream.ReadBooleanAsync();
        }

        public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

        public Task WriteAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}
