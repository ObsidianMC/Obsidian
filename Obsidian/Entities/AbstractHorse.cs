using Obsidian.Net;

using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public class AbstractHorse : Animal
{
    public HorseMask HorseMask { get; set; }

    public Guid Owner { get; set; }
    internal int Temper { get; set; }
    internal bool HasRider => Rider != null;
    protected virtual bool CanBreedHorse => false;
    protected virtual int MaximumTemper => 100;
    protected virtual bool SupportsSaddle => true;
    protected virtual bool IsBreedingFood(Material food) => food is Material.GoldenCarrot or Material.GoldenApple;
    protected override string? SoundName => Type == EntityType.ZombieHorse ? "zombie_horse" : "horse";
    internal override float JumpPower => GetAttributeValue("minecraft:horse.jump_strength");

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 1.2f));
        if (CanBreedHorse)
        {
            actions.AddGoal(2, new BreedGoal(this));
            actions.AddGoal(3, new FollowParentGoal(this));
            actions.AddGoal(5, new HorseGrazeGoal(this));
        }
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
    }

    protected override bool CanEat(ItemStack? item) => CanBreedHorse && item is { Count: > 0 } &&
        item.Type is Material.Wheat or Material.Sugar or Material.Apple or Material.HayBlock or Material.GoldenCarrot or Material.GoldenApple;

    internal override async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        if (!HasAi || !Alive || IsRemoved || player.Health <= 0 || player.Level != Level ||
            player.Gamemode == Gamemode.Spectator || !IsInRange(player, 4) || !CanSee(player))
            return;
        var slot = hand == Hand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        var tamed = HorseMask.HasFlag(HorseMask.Tamed);
        if (CanEat(item))
        {
            var food = item!.Type;
            var heal = food switch { Material.HayBlock => 20, Material.Apple => 3, Material.GoldenCarrot => 4, Material.GoldenApple => 10, _ => 2 };
            var breedingFood = IsBreedingFood(food);
            if (Health >= GetAttributeValue("minecraft:generic.max_health") && !IsBaby &&
                (tamed || Temper >= MaximumTemper) && !(tamed && breedingFood && Age == 0 && LoveTicks == 0))
                return;
            Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + heal);
            Temper = Math.Min(MaximumTemper, Temper + (breedingFood ? 5 : 3));
            if (IsBaby)
                Age = Math.Min(0, Age + (food == Material.HayBlock ? 3600 : 1200));
            else if (tamed && breedingFood && Age == 0)
            {
                LoveTicks = 600;
                SendEntityEvent(18);
            }
            await ConsumeItemAsync(player, slot);
            SynchronizeMetadata();
            return;
        }
        if (IsBaby)
            return;
        if (SupportsSaddle && tamed && !HasSaddle && item is { Count: > 0, Type: Material.Saddle })
        {
            HasSaddle = true;
            await ConsumeItemAsync(player, slot);
            return;
        }
        if (!player.Sneaking && player is Player rider && Mount(rider))
            GoalController?.Pause();
    }

    private static async ValueTask ConsumeItemAsync(IPlayer player, int slot)
    {
        if (player.Gamemode == Gamemode.Creative)
            return;
        player.Inventory.RemoveItem(slot, 1);
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
    }

    public bool HasSaddle
    {
        get => !GetEquipment(EquipmentSlot.Saddle).IsAir;
        set => SetEquipment(EquipmentSlot.Saddle, value ? ItemsRegistry.GetSingleItem(Material.Saddle) : ItemStack.Air);
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (HasSaddle)
            DropItem(Material.Saddle);
        return default;
    }

    protected override void TickRidden()
    {
        base.TickRidden();
        if (Rider == null)
        {
            if (GoalController is GoalSelector { IsPaused: true } goals)
                goals.Resume();
            return;
        }
        if (!HorseMask.HasFlag(HorseMask.Tamed))
        {
            if (Random.Next(50) != 0)
                return;
            if (Random.Next(MaximumTemper) < Temper)
            {
                HorseMask |= HorseMask.Tamed;
                Owner = Rider.Uuid;
                PersistenceRequired = true;
                SendEntityEvent(7);
                SynchronizeMetadata();
            }
            else
            {
                Temper = Math.Min(MaximumTemper, Temper + 5);
                Dismount();
                SendEntityEvent(6);
            }
            return;
        }
        if (!SupportsSaddle || !HasSaddle)
            return;
        Yaw = Rider.Yaw;
        Pitch = Rider.Pitch.Degrees * 0.5f;
        var input = Rider.Input;
        var forward = (input.HasFlag(PlayerInput.Forward) ? 1f : 0) - (input.HasFlag(PlayerInput.Backward) ? 1f : 0);
        var sideways = (input.HasFlag(PlayerInput.Left) ? 0.5f : 0) - (input.HasFlag(PlayerInput.Right) ? 0.5f : 0);
        MoveControl.Ride(MovementSpeed, forward < 0 ? forward * 0.25f : forward, sideways);
        if (input.HasFlag(PlayerInput.Jump))
            JumpControl.Jump();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)HorseMask);
    }
}

internal sealed class HorseGrazeGoal(AbstractHorse horse) : Goal
{
    private int ticks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => !horse.HasRider && !horse.IsBaby && horse.Random.Next(300) == 0 &&
        horse.Terrain.GetBlock((Vector)(horse.Position - new VectorF(0, 0.01f, 0)).Floor())?.Material == Material.GrassBlock;
    public override bool CanContinue() => ticks > 0 && !horse.HasRider;
    public override void Start()
    {
        ticks = 50;
        (horse.Navigator as Navigator)?.Stop();
        horse.HorseMask |= HorseMask.Eating;
        horse.SynchronizeMetadata();
    }
    public override ValueTask TickAsync() { ticks--; return default; }
    public override void Stop()
    {
        horse.HorseMask &= ~HorseMask.Eating;
        horse.SynchronizeMetadata();
    }
}

public enum HorseMask
{
    None,

    Tamed = 0x02,
    Saddled = 0x04,
    HasBred = 0x08,
    Eating = 0x10,
    Rearing = 0x20,
    MouthOpen = 0x40
}
