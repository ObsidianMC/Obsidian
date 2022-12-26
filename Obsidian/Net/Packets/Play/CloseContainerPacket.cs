using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play;

public partial class CloseContainerPacket : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public byte WindowId { get; private set; }

    public int Id => 0x0F;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        if (WindowId == 0 || (player.OpenedContainer is not IBlockEntity tileEntity))
            return;

        var position = tileEntity.BlockPosition;

        var block = await player.World.GetBlockAsync(position);

        if (block == null)
            return;

        if (block.Is(Material.Chest))
        {
            await player.client.QueuePacketAsync(new BlockActionPacket
            {
                Position = position,
                ActionId = 1,
                ActionParam = 0,
                BlockType = block.BaseId
            });
            await player.SendSoundAsync(Sounds.BlockChestClose, position.SoundPosition);
        }
        else if (block.Is(Material.EnderChest))
        {
            await player.client.QueuePacketAsync(new BlockActionPacket
            {
                Position = position,
                ActionId = 1,
                ActionParam = 0,
                BlockType = block.BaseId
            });
            await player.SendSoundAsync(Sounds.BlockEnderChestClose, position.SoundPosition);
        }

        player.OpenedContainer = null;
    }
}
