using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:witch")]
public sealed partial class Witch : PathfinderMob
{
    private int drinkTicks;
    private int drinkEffect = -1;
    internal bool Drinking => drinkTicks > 0;
    public Witch() => Type = EntityType.Witch;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "witch";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    internal override float MovementSpeed => Drinking ? Math.Max(0, base.MovementSpeed - 0.25f) : base.MovementSpeed;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(2, new WitchThrowPotionGoal(this));
        actions.AddGoal(2, new RandomStrollGoal(this, 1));
        actions.AddGoal(3, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(3, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
    }
    protected override ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return default;
        if (Drinking)
        {
            if (--drinkTicks == 0)
            {
                if (drinkEffect == (int)PotionEffect.InstantHealth - 1)
                    Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 4);
                else AddPotionEffect(drinkEffect, 3600);
                SetEquipment(EquipmentSlot.MainHand, ItemStack.Air);
                SynchronizeMetadata();
            }
            return default;
        }
        var effect = -1;
        if (Random.NextSingle() < 0.15f && Terrain.GetBlock((Vector)EyePosition.Floor())?.Material == Material.Water && !HasPotionEffect((int)PotionEffect.WaterBreathing - 1)) effect = (int)PotionEffect.WaterBreathing - 1;
        else if (Random.NextSingle() < 0.15f && (Burning || InLava) && !HasPotionEffect((int)PotionEffect.FireResistance - 1)) effect = (int)PotionEffect.FireResistance - 1;
        else if (Random.NextSingle() < 0.05f && Health < GetAttributeValue("minecraft:generic.max_health")) effect = (int)PotionEffect.InstantHealth - 1;
        else if (Random.NextSingle() < 0.5f && AttackTarget is { } target && !IsInRange(target, 11) && !HasPotionEffect((int)PotionEffect.Speed - 1)) effect = (int)PotionEffect.Speed - 1;
        if (effect >= 0)
        {
            drinkEffect = effect;
            drinkTicks = 32;
            SetEquipment(EquipmentSlot.MainHand, MakePotion(Material.Potion, effect, 3600));
            PlayMobSound("drink");
            SynchronizeMetadata();
        }
        return default;
    }
    internal static ItemStack MakePotion(Material material, int effect, int duration, int amplifier = 0) =>
        new(ItemsRegistry.Get(material), 1, new PotionContentsDataComponent
        { CustomEffects = effect >= 0 ? [new PotionEffectData { Id = effect, Duration = duration, Amplifier = amplifier, ShowParticles = true, ShowIcon = true }] : [] });
    internal void ThrowPotion(IEntity target)
    {
        var effect = (int)PotionEffect.InstantDamage - 1;
        var duration = 0;
        if (target.Type is EntityType.Pillager or EntityType.Vindicator or EntityType.Evoker or EntityType.Illusioner or EntityType.Ravager)
        {
            effect = target.Health <= 4 ? (int)PotionEffect.InstantHealth - 1 : (int)PotionEffect.Regeneration - 1;
            duration = 900;
        }
        else if (target is Living living)
        {
            if (!IsInRange(target, 8) && !living.HasPotionEffect((int)PotionEffect.Slowness - 1)) { effect = (int)PotionEffect.Slowness - 1; duration = 1800; }
            else if (target.Health >= 8 && !living.HasPotionEffect((int)PotionEffect.Poison - 1)) { effect = (int)PotionEffect.Poison - 1; duration = 900; }
            else if (IsInRange(target, 3) && !living.HasPotionEffect((int)PotionEffect.Weakness - 1) && Random.NextSingle() < 0.25f) { effect = (int)PotionEffect.Weakness - 1; duration = 1800; }
        }
        var aim = target.Position + (target is Entity entity ? entity.Motion : VectorD.Zero) + new VectorD(0, target.Dimension.Height * 0.85f - 1.1f, 0) - EyePosition;
        aim.Y += Math.Sqrt(aim.X * aim.X + aim.Z * aim.Z) * 0.2;
        aim += new VectorD(Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle()) * 0.13782;
        Level.SpawnEntity(new MobProjectile(this, EntityType.SplashPotion, EyePosition, aim) { SplashEffect = effect, SplashDuration = duration });
        PlayMobSound("throw");
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        Material[] materials = [Material.GlowstoneDust, Material.Sugar, Material.GlassBottle, Material.SpiderEye, Material.Gunpowder, Material.Stick, Material.Stick];
        for (var i = Random.Next(1, 4); i > 0; i--) DropItem(materials[Random.Next(materials.Length)], Random.Next(3));
        DropItem(Material.Redstone, Random.Next(4, 9));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(Drinking);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("ObsidianDrinkTicks", drinkTicks);
        writer.WriteInt("ObsidianDrinkEffect", drinkEffect);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        drinkTicks = Math.Clamp(tag.TryGetTagValue<int>("ObsidianDrinkTicks", out var ticks) ? ticks : 0, 0, 32);
        drinkEffect = tag.TryGetTagValue<int>("ObsidianDrinkEffect", out var effect) ? effect : -1;
        if (drinkEffect is not (5 or 11 or 12 or 0)) drinkTicks = 0;
    }
}

internal sealed class WitchThrowPotionGoal(Witch witch) : NavigationGoal(witch, 1)
{
    private long nextThrow;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => witch.AttackTarget is { } target && witch.IsValidTarget(target);
    public override void Start() => nextThrow = witch.AiTick + 60;
    public override ValueTask TickAsync()
    {
        var target = witch.AttackTarget!;
        witch.LookControl.LookAt(target);
        if (!witch.IsInRange(target, 10) || !witch.CanSee(target)) MoveTo(target);
        else Navigation.Stop();
        if (!witch.Drinking && witch.AiTick >= nextThrow && witch.CanSee(target) && witch.IsInRange(target, 10))
        {
            var ally = witch.GetEntitiesNear(16).OfType<Mob>().FirstOrDefault(mob => mob.Health < mob.GetAttributeValue("minecraft:generic.max_health") &&
                (mob.Type is EntityType.Pillager or EntityType.Vindicator or EntityType.Evoker or EntityType.Illusioner or EntityType.Ravager) && witch.CanSee(mob));
            witch.ThrowPotion((IEntity?)ally ?? target);
            nextThrow = witch.AiTick + 60;
        }
        return default;
    }
}
