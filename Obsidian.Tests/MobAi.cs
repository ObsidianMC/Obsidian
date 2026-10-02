using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Utilities;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobAi
{
    [Fact]
    public void WorldsWithoutSavedDifficultyAllowHostileMobs()
    {
        Assert.Equal(Difficulty.Normal, new LevelData().Difficulty);
    }

    [Fact]
    public void FractionalNegativePositionsBelongToThePreviousChunk()
    {
        Assert.Equal((-1, -2), new VectorF(-0.1f, 0, -16.1f).ToChunkCoord());
        Assert.Equal((0, 1), new VectorF(0.1f, 0, 16.1f).ToChunkCoord());
    }

    [Fact]
    public async Task HigherPriorityMovementInterruptsWanderingButPreservesLooking()
    {
        var selector = new GoalSelector();
        var wander = new ProbeGoal(GoalFlags.Move) { Enabled = true };
        var look = new ProbeGoal(GoalFlags.Look) { Enabled = true };
        var panic = new ProbeGoal(GoalFlags.Move);
        selector.AddGoal(6, wander);
        selector.AddGoal(7, look);
        selector.AddGoal(1, panic);

        await selector.TickAsync();
        panic.Enabled = true;
        await selector.TickAsync();

        Assert.Equal(1, wander.Starts);
        Assert.Equal(1, wander.Stops);
        Assert.Equal(1, panic.Starts);
        Assert.Equal(1, look.Starts);
        Assert.Equal(0, look.Stops);
        selector.Pause();
        await selector.TickAsync();
        Assert.False(selector.IsExecuting);
        Assert.Equal(1, panic.Stops);
        Assert.Equal(1, look.Stops);
    }

    [Fact]
    public void CollisionClipsAtWallAndFloorWithoutBlockingSliding()
    {
        var bounds = new BoundingBox(new VectorF(0.1f, 1, 0.1f), new VectorF(0.9f, 2, 0.9f));
        BoundingBox[] obstacles =
        [new(new VectorF(1, 0, 0), new VectorF(2, 3, 1)), new(new VectorF(0, 0, 0), new VectorF(1, 1, 1))];

        var displacement = EntityMovement.Clip(bounds, new VectorF(0.5f, -0.2f, 0.3f), obstacles);

        Assert.Equal(0.1f, displacement.X, 4);
        Assert.Equal(0, displacement.Y);
        Assert.Equal(0.3f, displacement.Z, 4);
    }

    [Fact]
    public void BabiesUseHalfSizedBoundingBoxesAndZombieSpeedBonus()
    {
        var pig = new Pig { Level = null! };
        var zombie = new Zombie { Level = null! };
        var adultPigWidth = pig.Dimension.Width;
        var adultZombieHeight = zombie.Dimension.Height;
        var adultZombieSpeed = zombie.MovementSpeed;
        pig.IsBaby = true;
        zombie.IsBaby = true;

        Assert.Equal(adultPigWidth / 2, pig.Dimension.Width);
        Assert.Equal(adultZombieHeight / 2, zombie.Dimension.Height);
        Assert.Equal(adultZombieSpeed * 1.5f, zombie.MovementSpeed);
        Assert.Equal(-24000, pig.Age);
    }

    [Fact]
    public void ImportedShapesUseDefaultStatesForAirAndFullBlocks()
    {
        Assert.Null(BlocksRegistry.Air.State);
        Assert.Null(BlocksRegistry.Stone.State);
        Assert.Empty(BlockCollisionShapes.Get(BlocksRegistry.Air));
        var stone = Assert.Single(BlockCollisionShapes.Get(BlocksRegistry.Stone));
        Assert.Equal(VectorF.Zero, stone.Min);
        Assert.Equal(new VectorF(1), stone.Max);
    }

    private sealed class ProbeGoal(GoalFlags flags) : Goal
    {
        public bool Enabled { get; set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public override GoalFlags Flags => flags;
        public override bool CanUse() => Enabled;
        public override void Start() => Starts++;
        public override void Stop() => Stops++;
    }
}
