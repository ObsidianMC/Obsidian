

using Obsidian.Entities.AI;

namespace Obsidian.Entities;

public class Animal : AgeableMob
{
    // Vanilla Animal: fire is a danger to walk near (16) and damages when stood in (-1).
    public Animal()
    {
        SetPathfindingMalus(PathType.DANGER_FIRE, 16f);
        SetPathfindingMalus(PathType.DAMAGE_FIRE, -1f);
    }

    public int LoveTicks { get; internal set; }
    internal virtual bool CanBreed => Alive && !IsRemoved && Age == 0 && LoveTicks > 0;
    internal virtual bool CanMateWith(Animal mate) => !ReferenceEquals(this, mate) && mate.Type == Type && mate.CanBreed;
    protected virtual bool CanEat(Obsidian.API.Inventory.ItemStack? item) => false;
    internal override ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand) => FeedAsync(player, hand);

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (LoveTicks > 0)
        {
            LoveTicks--;
            if (LoveTicks % 10 == 0)
                SendEntityEvent(18);
        }
        if (Age != 0)
            LoveTicks = 0;
    }

    /// <summary>
    /// The animal's interaction (<see cref="InteractAsync"/>): feeding it by default, which grows a baby or makes an
    /// adult ready to breed.
    /// </summary>
    /// <returns>Whether the animal used the interaction.</returns>
    internal virtual async ValueTask<bool> FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.GameMode == GameMode.Spectator || player.Level != Level ||
            !IsInRange(player, 4) || !CanSee(player))
            return false;

        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (!CanEat(item) || !IsBaby && (Age != 0 || LoveTicks > 0))
            return false;

        if (IsBaby)
            Age += (-Age / 20 / 10) * 20;
        else
        {
            LoveTicks = 600;
            SendEntityEvent(18);
        }

        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(slot, 1);
            await player.SendInventorySlotAsync(slot);
        }
        SynchronizeMetadata();
        return true;
    }

    internal void BreedWith(Animal mate)
    {
        if (!CanBreed || !CanMateWith(mate) || mate.Level != Level || !IsInRange(mate, 3))
            return;

        CreateOffspring(mate);
        Age = mate.Age = 6000;
        LoveTicks = mate.LoveTicks = 0;
        Level.SpawnExperienceOrbs(Position, (short)Random.Next(1, 8));
    }

    protected virtual IEntity CreateOffspring(Animal mate) =>
        Level.GetNewEntitySpawner().WithEntityType(Type!.Value).AtPosition(Position).AsBaby().Spawn();

    public async override ValueTask TickAsync()
    {
        // TODO obby doesn't properly spawn entities yet
        var closest = HasAi ? null : Level.PlayersInRange((Vector)Position).MinBy(p => VectorD.Distance(Position, p.Position));
        if (closest is not null)
        {
            var closestPosition = new VectorD(closest.Position.X, closest.HeadY, closest.Position.Z);

            var lookAt = closestPosition - Position;

            var yaw = (byte)((Math.Atan2(lookAt.Z, lookAt.X) * (256 / (2 * Math.PI)) - 64) % 256);
            var pitch = (byte)(256 - (Math.Asin(lookAt.Y / lookAt.Magnitude) * (256 / (2 * Math.PI))));

            SetRotation(new Angle(yaw), new Angle(pitch), MovementFlags.OnGround);
            SetHeadRotation(new Angle(yaw));
        }

        await base.TickAsync();
    }
}
