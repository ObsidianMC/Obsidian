using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // Vanilla's Player.canPlayerFitWithinBlocksAndEntitiesWhen shrinks the pose's box by this much on every side.
    private const double FitMargin = 1.0E-7;

    /// <summary>
    /// The player's box, which their <see cref="Entity.Pose"/> decides, as vanilla's <c>Avatar.POSES</c> gives it:
    /// 0.6 wide and 1.8 tall standing, 1.5 tall sneaking, 0.6 tall swimming, crawling, gliding or spinning with a
    /// trident, and 0.2 by 0.2 asleep or dying. The scale attribute scales every box but those last two, which vanilla
    /// fixes.
    /// </summary>
    /// <remarks>
    /// The contact pass, pressure plates, tripwires and collision all read it, so they see the box the player's pose
    /// gives them. The entity data's 0.6 by 1.8 is the standing box, so no box is generated for players.
    /// </remarks>
    public sealed override EntityDimension Dimension => DimensionOf(this.Pose);

    /// <summary>The box <paramref name="pose"/> gives the player, scaled by their scale attribute.</summary>
    internal EntityDimension DimensionOf(Pose pose)
    {
        var (width, height, scalable) = pose switch
        {
            Pose.Sneaking => (0.6f, 1.5f, true),
            Pose.Swimming or Pose.FallFlying or Pose.SpinAttack => (0.6f, 0.6f, true),
            Pose.Sleeping or Pose.Dying => (0.2f, 0.2f, false),
            _ => (0.6f, 1.8f, true),
        };
        var scale = scalable ? this.AttributeScale : 1;
        return new EntityDimension { Width = width * scale, Height = height * scale };
    }

    /// <summary>
    /// Works out the player's pose from what they're doing, as vanilla's <c>Player.updatePlayerPose</c> does each tick:
    /// asleep, swimming, gliding, spinning with a trident or sneaking (not while flying), or else standing. Where that
    /// pose's box doesn't fit between the blocks around them, they crouch, or crawl (the swimming pose) where crouching
    /// doesn't fit either; with no room even to crawl, the pose stays as it was. Their watchers hear of a change.
    /// </summary>
    internal void UpdatePose()
    {
        var terrain = new MobTerrain(this.Level);
        if (!this.Fits(terrain, Pose.Swimming))
            return;

        var desired = this.DesiredPose();
        Pose pose;
        if (this.GameMode == GameMode.Spectator || this.Vehicle is not null || this.Fits(terrain, desired))
            pose = desired;
        else if (this.Fits(terrain, Pose.Sneaking))
            pose = Pose.Sneaking;
        else
            pose = Pose.Swimming;

        if (pose == this.Pose)
            return;

        this.Pose = pose;
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new SetEntityDataPacket
        {
            EntityId = this.EntityId,
            Entity = this
        }, this.EntityId);
    }

    // The pose comes from UpdatePose, not from the flags as it's sent.
    protected override void SyncPoseWithFlags()
    {
    }

    // Vanilla's Player.getDesiredPose.
    private Pose DesiredPose()
    {
        if (this.Sleeping)
            return Pose.Sleeping;

        if (this.Swimming)
            return Pose.Swimming;

        if (this.FlyingWithElytra)
            return Pose.FallFlying;

        if (this.LivingBitMask.HasFlag(LivingBitMask.InRiptideSpinAttack))
            return Pose.SpinAttack;

        var flying = this.Abilities.HasFlag(PlayerAbility.Flying);
        return this.Sneaking && !flying ? Pose.Sneaking : Pose.Standing;
    }

    // Whether the pose's box fits where the player is. Only blocks are checked, not the entities vanilla also counts.
    private bool Fits(MobTerrain terrain, Pose pose)
    {
        var box = this.DimensionOf(pose).CreateBBFromPosition(this.Position);
        var margin = new VectorD(FitMargin, FitMargin, FitMargin);
        var shrunk = new BoundingBox(box.Min + margin, box.Max - margin);
        return terrain.IsFreeFor(shrunk, this);
    }
}
