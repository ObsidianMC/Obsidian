using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;
using Obsidian.Entities;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class UseItemPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public InteractionHand Hand { get; private set; }


    [Field(1), VarLength]
    public int Sequence { get; private set; }

    [Field(2)]
    public float Yaw { get; private set; }

    [Field(3)]
    public float Pitch { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<InteractionHand>();
        this.Sequence = reader.ReadVarInt();
        this.Yaw = reader.ReadSingle();
        this.Pitch = reader.ReadSingle();
    }

    public override async ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        await this.UseAsync(server, player);

    internal async ValueTask<UseResult> UseAsync(IServer server, IClientPlayer player)
    {
        if (player is not Player user)
        {
            await player.UseItemAsync(this.Hand);
            return UseResult.Pass;
        }

        user.QueueBlockAcknowledgement(this.Sequence);
        if (!user.Alive || user.Respawning || user.HasPendingTeleport
            || !float.IsFinite(this.Yaw) || !float.IsFinite(this.Pitch))
        {
            var slot = this.Hand == InteractionHand.OffHand ? 45 : user.CurrentHeldItemSlot;
            await user.SyncInventorySlotAsync(slot);
            return UseResult.Fail;
        }

        // Vanilla handleUseItem applies the packet's aim before an item's own POV ray trace.
        user.Yaw = this.Yaw;
        user.Pitch = Math.Clamp(this.Pitch, -90, 90);
        var stack = this.Hand == InteractionHand.OffHand ? user.GetOffHandItem() : user.GetHeldItem();
        if (BucketItems.Handles(stack))
            return await BucketItems.UseAsync(server, user, this.Hand, this.Sequence);

        await user.UseItemAsync(this.Hand);
        return UseResult.Pass;
    }
}
