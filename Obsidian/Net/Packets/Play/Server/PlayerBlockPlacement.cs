using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Util.Registry;

namespace Obsidian.Net.Packets.Play.Server
{
    public partial class PlayerBlockPlacement : IPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; set; } // hand it was placed from. 0 is main, 1 is off

        [Field(1)]
        public Position Position { get; set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public BlockFace Face { get; set; }

        [Field(3)]
        public float CursorX { get; set; }

        [Field(4)]
        public float CursorY { get; set; }

        [Field(5)]
        public float CursorZ { get; set; }

        [Field(6)]
        public bool InsideBlock { get; set; }

        public int Id => 0x2E;

        public PlayerBlockPlacement() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Hand = (Hand)await stream.ReadVarIntAsync();
            this.Position = await stream.ReadPositionAsync();
            this.Face = (BlockFace)await stream.ReadVarIntAsync();
            this.CursorX = await stream.ReadFloatAsync();
            this.CursorY = await stream.ReadFloatAsync();
            this.CursorZ = await stream.ReadFloatAsync();
            this.InsideBlock = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            var currentItem = player.GetHeldItem();

            var block = Registry.GetBlock(currentItem.Type);

            var location = this.Position;

            var interactedBlock = server.World.GetBlock(location);

            if (interactedBlock.IsInteractable && !player.Sneaking)
            {
                var arg = await server.Events.InvokeBlockInteractAsync(new BlockInteractEventArgs(player, block, this.Position));

                if (arg.Cancel)
                    return;

                //TODO open chests/Crafting inventory ^ ^

                //Logger.LogDebug($"Block Interact: {interactedBlock} - {location}");

                return;
            }

            if (player.Gamemode != Gamemode.Creative)
                player.Inventory.RemoveItem(player.CurrentSlot);

            switch (this.Face) // TODO fix this for logs
            {
                case BlockFace.Bottom:
                    location.Y -= 1;
                    break;

                case BlockFace.Top:
                    location.Y += 1;
                    break;

                case BlockFace.North:
                    location.Z -= 1;
                    break;

                case BlockFace.South:
                    location.Z += 1;
                    break;

                case BlockFace.West:
                    location.X -= 1;
                    break;

                case BlockFace.East:
                    location.X += 1;
                    break;

                default:
                    break;
            }

            server.World.SetBlock(location, block);

            await server.BroadcastBlockPlacementAsync(player, block, location);
        }
    }

}