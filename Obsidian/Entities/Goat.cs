using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:goat")]
public sealed partial class Goat : FarmAnimal
{
    private int ramCooldown = 600;
    private int longJumpCooldown = 600;
    private IEntity? ramTarget;
    private VectorF ramDestination;
    private int ramTicks;
    public Goat() => Type = EntityType.Goat;
    public bool Screaming { get; internal set; }
    public bool HasLeftHorn { get; internal set; } = true;
    public bool HasRightHorn { get; internal set; } = true;
    protected override string? SoundName => Screaming ? "goat.screaming" : "goat";
    protected override float SafeFallDistance => 10;
    protected override void FinalizeSpawn()
    {
        Screaming = Random.NextSingle() < 0.02f;
        ramCooldown = Screaming ? Random.Next(100, 301) : Random.Next(600, 6001);
        longJumpCooldown = Random.Next(600, 1201);
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Goat)base.CreateOffspring(mate);
        child.Screaming = Random.Next(2) == 0 ? Screaming : ((Goat)mate).Screaming;
        if (Random.NextSingle() < 0.02f) child.Screaming = true;
        child.SynchronizeMetadata();
        return child;
    }
    internal override async ValueTask InteractAsync(IPlayer player, Hand hand)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator || !IsInRange(player, 4)) return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!IsBaby && item is { Count: > 0, Type: Material.Bucket })
            await GiveInteractionItemAsync(player, hand, Material.MilkBucket);
        else await base.InteractAsync(player, hand);
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new GoatRamGoal(this));
    }
    internal bool StartRam()
    {
        if (ramCooldown > 0 || InWater || IsBaby || !MovementFlags.HasFlag(MovementFlags.OnGround)) return false;
        ramTarget = GetEntitiesNear(8).FirstOrDefault(entity => entity.Type != Type && IsValidTarget(entity) && CanSee(entity));
        if (ramTarget == null) return false;
        ramDestination = ramTarget.Position;
        ramTicks = 40;
        SendEntityEvent(58);
        return true;
    }
    internal bool IsRamming => ramTicks > 0 && ramTarget is { Health: > 0 } && ramTarget.Level == Level;
    internal async ValueTask RamAsync()
    {
        ((Navigator)Navigator!).Stop();
        if (--ramTicks > 20) { LookControl.LookAt(ramDestination); return; }
        var delta = ramDestination - Position;
        delta.Y = 0;
        if (delta.Magnitude > 0.01f) Motion = delta / delta.Magnitude * 0.6f + new VectorF(0, Motion.Y, 0);
        if (ramTarget != null && IsWithinMeleeRange(ramTarget))
        {
            await ramTarget.DamageAsync(this, 2);
            var away = ramTarget.Position - Position;
            away.Y = 0;
            if (away.Magnitude > 0.01f && ramTarget is Entity victim)
            {
                victim.Motion += away / away.Magnitude * 1.5f + new VectorF(0, 0.2f, 0);
                PacketBroadcaster.QueuePacketToLevelInRange(Level, victim.Position, new Obsidian.Net.Packets.Play.Clientbound.SetEntityMotionPacket
                { EntityId = victim.EntityId, Velocity = new Velocity(victim.Motion.X, victim.Motion.Y, victim.Motion.Z) });
            }
            ramTicks = 0;
            PlayMobSound("ram_impact");
        }
        else if (MovementFlags.HasFlag(MovementFlags.HorizontalCollision))
        {
            var hit = Terrain.GetBlock((Vector)(Position + (delta.Magnitude > 0.01f ? delta / delta.Magnitude : VectorF.Zero)).Floor());
            if ((HasLeftHorn || HasRightHorn) && hit != null && TagsRegistry.Block.SnapsGoatHorn.Entries.Contains(hit.RegistryId))
            {
                if (HasLeftHorn) HasLeftHorn = false; else HasRightHorn = false;
                var horn = ItemsRegistry.GetSingleItem(Material.GoatHorn);
                string[] instruments = ["ponder", "sing", "seek", "feel", "admire", "call", "yearn", "dream"];
                var instrumentName = $"minecraft:{instruments[Random.Next(4) + (Screaming ? 4 : 0)]}";
                var instrument = InstrumentsRegistry.All.FirstOrDefault(entry => entry.Identifier == instrumentName);
                if (instrument != null) horn[DataComponentType.Instrument] = Obsidian.API.Inventory.DataComponents.ComponentBuilder.Instrument with { Value = instrument.ToInstrumentData() };
                DropItem(horn);
                SynchronizeMetadata();
                PlayMobSound("horn_break");
            }
            ramTicks = 0;
        }
    }
    internal void FinishRam()
    {
        ramTicks = 0;
        ramTarget = null;
        ramCooldown = Screaming ? Random.Next(100, 301) : Random.Next(600, 6001);
        SendEntityEvent(59);
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (ramCooldown > 0) ramCooldown--;
        if (--longJumpCooldown <= 0 && !IsRamming && !InWater && MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            var landing = RandomPosition.Find(this, 5);
            if (landing is VectorF target && (target - Position).MagnitudeSquared() > 4)
            {
                var delta = target - Position;
                Motion = new VectorF(delta.X / 20, 0.9f, delta.Z / 20);
                PlayMobSound("long_jump");
            }
            longJumpCooldown = Random.Next(600, 1201);
        }
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(Screaming);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(!IsBaby && HasLeftHorn);
        writer.WriteEntityMetadataType(19, EntityMetadataType.Boolean); writer.WriteBoolean(!IsBaby && HasRightHorn);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteBool("IsScreamingGoat", Screaming);
        writer.WriteBool("HasLeftHorn", HasLeftHorn);
        writer.WriteBool("HasRightHorn", HasRightHorn);
        writer.WriteInt("ObsidianRamCooldown", ramCooldown);
        writer.WriteInt("ObsidianLongJumpCooldown", longJumpCooldown);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Screaming = tag.TryGetBool("IsScreamingGoat", out var screaming) && screaming;
        HasLeftHorn = !tag.TryGetBool("HasLeftHorn", out var left) || left;
        HasRightHorn = !tag.TryGetBool("HasRightHorn", out var right) || right;
        ramCooldown = Math.Clamp((tag.TryGetTagValue<int>("ObsidianRamCooldown", out var savedObsidianRamCooldown) ? savedObsidianRamCooldown : 0), 0, 6000);
        longJumpCooldown = Math.Clamp((tag.TryGetTagValue<int>("ObsidianLongJumpCooldown", out var savedObsidianLongJumpCooldown) ? savedObsidianLongJumpCooldown : 0), 0, 1200);
    }
}

internal sealed class GoatRamGoal(Goat goat) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => goat.StartRam();
    public override bool CanContinue() => goat.IsRamming;
    public override void Start() => ((Navigator)goat.Navigator!).Stop();
    public override ValueTask TickAsync() => goat.RamAsync();
    public override void Stop() => goat.FinishRam();
}
