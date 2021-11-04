using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PlayerBlockPlacement : IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

        [Field(1)]
        public Vector Position { get; private set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public BlockFace Face { get; private set; }

        [Field(3), DataFormat(typeof(float))]
        public VectorF Cursor { get; private set; }

        [Field(6)]
        public bool InsideBlock { get; private set; }

        public int Id => 0x2E;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            var currentItem = player.GetHeldItem();

            var itemType = currentItem.Type;

            // TODO: this better
            if (itemType == Material.WaterBucket)
                itemType = Material.Water;
            if (itemType == Material.LavaBucket)
                itemType = Material.Lava;

            Block block;
            try
            {
                block = Registry.GetBlock(itemType);
            }
            catch //item is not a block so just return
            {
                return;
            }

            var position = this.Position;

            var b = server.World.GetBlock(position);
            if (b is null) { return; }
            var interactedBlock = (Block)b;

            //TODO check if a player can place a block if they can't then call the player interact event
            if (interactedBlock.IsInteractable && !player.Sneaking)
            {
                await server.Events.InvokeAsync(Event.PlayerInteract, new PlayerInteractEventArgs(player)
                {
                    Item = player.MainHand == Hand.MainHand ? player.GetHeldItem() : player.GetOffHandItem(),
                    Block = interactedBlock,
                    BlockLocation = Position,
                });
                return;
            }

            if (player.Gamemode != Gamemode.Creative)
                player.Inventory.RemoveItem(player.inventorySlot);

            switch (Face) // TODO fix this for logs
            {
                case BlockFace.Bottom:
                    position.Y -= 1;
                    break;

                case BlockFace.Top:
                    position.Y += 1;
                    break;

                case BlockFace.North:
                    position.Z -= 1;
                    break;

                case BlockFace.South:
                    position.Z += 1;
                    break;

                case BlockFace.West:
                    position.X -= 1;
                    break;

                case BlockFace.East:
                    position.X += 1;
                    break;

                default:
                    break;
            }

            // TODO calculate the block state
            server.World.SetBlock(position, block, doBlockUpdate: true);
        }
    }
}
