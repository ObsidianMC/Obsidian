

namespace Obsidian.Entities;

public class Animal : AgeableMob
{
    public int LoveTicks { get; internal set; }
    internal bool CanBreed => Alive && !IsRemoved && Age == 0 && LoveTicks > 0;
    protected virtual bool CanEat(Obsidian.API.Inventory.ItemStack? item) => false;
    internal override ValueTask InteractAsync(IPlayer player, Hand hand) => FeedAsync(player, hand);

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

    internal virtual async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Gamemode == Gamemode.Spectator || player.Level != Level ||
            !IsInRange(player, 4) || !CanSee(player))
            return;

        var slot = hand == Hand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (!CanEat(item) || !IsBaby && (Age != 0 || LoveTicks > 0))
            return;

        if (IsBaby)
            Age += (-Age / 20 / 10) * 20;
        else
        {
            LoveTicks = 600;
            SendEntityEvent(18);
        }

        if (player.Gamemode != Gamemode.Creative)
        {
            player.Inventory.RemoveItem(slot, 1);
            await player.Client.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerSetSlotPacket
            {
                ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot)
            });
        }
        SynchronizeMetadata();
    }

    internal void BreedWith(Animal mate)
    {
        if (!CanBreed || !mate.CanBreed || mate.Level != Level || !IsInRange(mate, 3))
            return;

        CreateOffspring(mate);
        Age = mate.Age = 6000;
        LoveTicks = mate.LoveTicks = 0;
        Level.SpawnExperienceOrbs(Position, (short)Random.Next(1, 8));
    }

    protected virtual IEntity CreateOffspring(Animal mate) =>
        Level.GetNewEntitySpawner().WithEntityType(Type).AtPosition(Position).AsBaby().Spawn();

    public async override ValueTask TickAsync()
    {
        // TODO obby doesn't properly spawn entities yet
        var players = HasAi ? Enumerable.Empty<IPlayer>() : Level.PlayersInRange((Vector)Position);
        if (players.Any())
        {
            var closest = players.OrderBy(p => VectorF.Distance(Position, p.Position)).First();
            var closestPosition = new VectorF()
            {
                X = closest.Position.X,
                Y = (float)closest.HeadY,
                Z = closest.Position.Z
            };

            var lookAt = closestPosition - Position;

            var yaw = (byte)((MathF.Atan2(lookAt.Z, lookAt.X) * (256 / (2 * MathF.PI)) - 64) % 256);
            var pitch = (byte)(256 - (MathF.Asin(lookAt.Y / lookAt.Magnitude) * (256 / (2 * MathF.PI))));

            SetRotation(new Angle(yaw), new Angle(pitch), MovementFlags.OnGround);
            SetHeadRotation(new Angle(yaw));
        }

        await base.TickAsync();
    }
}
