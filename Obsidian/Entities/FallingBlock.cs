using Obsidian.API;
using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class FallingBlock : Entity
    {
        public VectorF SpawnPosition { get; private set; }

        public Material BlockMaterial { get; set; }

        private int AliveTime { get; set; }

        private VectorF DeltaPosition { get; set; }

        private readonly VectorF gravity = new VectorF(0F, -0.004F, 0F);
        
        private readonly float windResFactor = 0.98F;

        public FallingBlock() : base()
        {
            SpawnPosition = Position;
            LastPosition = Position;
            AliveTime = 0;
            DeltaPosition = new VectorF(0F, 0F, 0F);
        }

        public async override Task TickAsync()
        {
            AliveTime++;
            LastPosition = Position;
            if (!this.NoGravity)
                DeltaPosition += gravity; // y - 0.04
            DeltaPosition *= windResFactor; // * 0.98
            Position += DeltaPosition;

            // Check below to see if we're about to hit a solid block.
            var upcomingBlockPos = new Vector(
                (int)Math.Round(Position.X),
                (int)Math.Round(Position.Y) - 1,
                (int)Math.Round(Position.Z));

            bool convertToBlock = !server.World.GetBlock(upcomingBlockPos)?.IsAir ?? false;
            if (convertToBlock) { await ConvertToBlock(upcomingBlockPos + (0, 1, 0)); }
        }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            //await stream.WriteEntityMetdata(7, EntityMetadataType.VarInt, SpawnPosition);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

/*            stream.WriteEntityMetadataType(7, EntityMetadataType.VarInt);
            stream.WriteVarInt(SpawnPosition);*/
        }

        private async Task ConvertToBlock(Vector loc)
        {
            var block = new Block(BlockMaterial);
            server.World.SetBlock(loc, block);

            foreach (var p in server.PlayersInRange(loc))
            {
                await server.BroadcastBlockPlacementToPlayerAsync(p, block, loc);
            }

            await server.World.DestroyEntityAsync(this);
        }
    }
}
