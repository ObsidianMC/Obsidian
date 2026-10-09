using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities.AI;

public sealed class BreakDoorGoal(Zombie mob) : Goal
{
    private Vector door;
    private int ticks;
    private int lastStage = -1;
    public override GoalFlags Flags => GoalFlags.Move;
    public override bool RequiresUpdateEveryTick => true;

    public override bool CanUse()
    {
        if (!mob.CanBreakDoors || mob.Level.LevelData.Difficulty != Difficulty.Hard ||
            !mob.MovementFlags.HasFlag(MovementFlags.HorizontalCollision))
            return false;
        var origin = (Vector)mob.Position.Floor();
        for (var x = origin.X - 1; x <= origin.X + 1; x++)
        for (var z = origin.Z - 1; z <= origin.Z + 1; z++)
        {
            var point = new Vector(x, origin.Y, z);
            var block = mob.Terrain.GetBlock(point);
            if (block != null && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId) &&
                BlockCollisionShapes.Get(block).Any(shape =>
                    MobTerrain.Overlaps(mob.Dimension.CreateBBFromPosition(mob.Position + mob.Motion + mob.MoveControl.Acceleration), shape.OffsetBy((VectorD)point))))
            {
                door = point;
                return true;
            }
        }
        return false;
    }

    public override void Start()
    {
        ticks = 0;
        lastStage = -1;
        ((Navigator)mob.Navigator!).Stop();
    }

    public override bool CanContinue() => mob.CanBreakDoors && mob.Alive && ticks < 240 &&
        mob.Level.LevelData.Difficulty == Difficulty.Hard && (mob.Position - ((VectorD)door + 0.5f)).MagnitudeSquared() < 4 &&
        mob.Terrain.GetBlock(door) is { } block && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId);

    public override async ValueTask TickAsync()
    {
        mob.MoveControl.Stop();
        var stage = Math.Min(9, ++ticks * 10 / 240);
        if (stage != lastStage)
        {
            lastStage = stage;
            SendProgress(stage);
        }
        if (ticks != 240)
            return;

        var block = mob.Terrain.GetBlock(door);
        await mob.Level.SetBlockAsync(door, BlocksRegistry.Air, true);
        var upper = door + new Vector(0, 1, 0);
        if (mob.Terrain.GetBlock(upper)?.Material == block?.Material)
            await mob.Level.SetBlockAsync(upper, BlocksRegistry.Air, true);
    }

    public override void Stop() => SendProgress(-1);

    private void SendProgress(int stage) => mob.PacketBroadcaster.QueuePacketToLevelInRange(mob.Level, mob.Position,
        new BlockDestructionPacket { EntityId = mob.EntityId, Position = door, DestroyStage = (sbyte)stage }, mob.EntityId);
}
