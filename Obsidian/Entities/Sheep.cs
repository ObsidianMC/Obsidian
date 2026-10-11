using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:sheep")]
public sealed partial class Sheep : FarmAnimal
{
    private static readonly string[] colors = ["White", "Orange", "Magenta", "LightBlue", "Yellow", "Lime", "Pink", "Gray", "LightGray", "Cyan", "Purple", "Blue", "Brown", "Green", "Red", "Black"];
    public Sheep() => Type = EntityType.Sheep;
    public byte Color { get; set; }
    public bool Sheared { get; set; }
    protected override string? SoundName => "sheep";
    protected override float TemptSpeed => 1.1f;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(5, new EatGrassGoal(this));
    }

    protected override void FinalizeSpawn()
    {
        var value = Random.Next(100);
        var variant = GetFarmVariant();
        Color = variant switch
        {
            0 => value < 5 ? (byte)8 : value < 10 ? (byte)7 : value < 15 ? (byte)0 : value < 18 ? (byte)12 :
                Random.Next(500) == 0 ? (byte)6 : (byte)15,
            2 => value < 5 ? (byte)7 : value < 10 ? (byte)8 : value < 15 ? (byte)0 : value < 18 ? (byte)15 :
                Random.Next(500) == 0 ? (byte)6 : (byte)12,
            _ => value < 5 ? (byte)15 : value < 10 ? (byte)7 : value < 15 ? (byte)8 : value < 18 ? (byte)12 :
                Random.Next(500) == 0 ? (byte)6 : (byte)0
        };
    }

    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.Health <= 0 || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player))
            return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!IsBaby && !Sheared && held is { Count: > 0, Type: Material.Shears })
        {
            Sheared = true;
            DropItem(Enum.Parse<Material>(colors[Color] + "Wool"), Random.Next(1, 4));
            await DamageInteractionToolAsync(player, hand);
            SynchronizeMetadata();
            return;
        }
        if (held is { Count: > 0 })
        {
            var dye = Array.FindIndex(colors, color => held.Type.ToString() == color + "Dye");
            if (dye >= 0 && Color != dye)
            {
                Color = (byte)dye;
                if (player.GameMode != GameMode.Creative)
                {
                    player.Inventory.RemoveItem(hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot, 1);
                    var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
                    await player.SendInventorySlotAsync(slot);
                }
                SynchronizeMetadata();
                return;
            }
        }
        await base.FeedAsync(player, hand);
    }

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Sheep)base.CreateOffspring(mate);
        child.Color = MixColors(Color, ((Sheep)mate).Color) ?? (Random.Next(2) == 0 ? Color : ((Sheep)mate).Color);
        child.SynchronizeMetadata();
        return child;
    }

    internal static byte? MixColors(byte first, byte second)
    {
        var pair = first < second ? (first, second) : (second, first);
        return pair switch
        {
            (0, 15) => 7, (0, 7) => 8, (0, 11) => 3, (0, 13) => 5, (0, 14) => 6,
            (4, 14) => 1, (11, 14) => 10, (11, 13) => 9, (6, 10) => 2,
            _ => null
        };
    }

    internal void EatGrass()
    {
        Sheared = false;
        if (IsBaby)
            Age = Math.Min(0, Age + 1200);
        SynchronizeMetadata();
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby)
        {
            DropItem(Burning ? Material.CookedMutton : Material.Mutton, Random.Next(1, 3));
            if (!Sheared)
                DropItem(Enum.Parse<Material>(colors[Color] + "Wool"));
        }
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)(Color | (Sheared ? 16 : 0)));
    }
}

internal sealed class EatGrassGoal(Sheep sheep) : Goal
{
    private int ticks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => sheep.Random.Next(sheep.IsBaby ? 50 : 1000) == 0 &&
        (sheep.Terrain.GetBlock((Vector)sheep.Position.Floor())?.Material == Material.ShortGrass ||
         sheep.Terrain.GetBlock((Vector)sheep.Position.Floor() - new Vector(0, 1, 0))?.Material == Material.GrassBlock);
    public override bool CanContinue() => ticks > 0;
    public override void Start()
    {
        ticks = 40;
        (sheep.Navigator as Navigator)?.Stop();
        sheep.SendEntityEvent(10);
    }
    public override async ValueTask TickAsync()
    {
        if (--ticks != 4)
            return;
        var position = (Vector)sheep.Position.Floor();
        if (sheep.Terrain.GetBlock(position)?.Material == Material.ShortGrass)
            await sheep.Level.SetBlockAsync(position, BlocksRegistry.Air, true);
        else
        {
            position -= new Vector(0, 1, 0);
            if (sheep.Terrain.GetBlock(position)?.Material != Material.GrassBlock)
                return;
            await sheep.Level.SetBlockAsync(position, BlocksRegistry.Dirt, true);
        }
        sheep.EatGrass();
    }
}
