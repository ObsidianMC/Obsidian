using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:magma_cube")]
public sealed partial class MagmaCube : Slime
{
    public MagmaCube() => Type = EntityType.MagmaCube;
    public override int Size
    {
        get => base.Size;
        set
        {
            base.Size = value;
            TryUpdateAttribute("minecraft:generic.armor", Size * 3);
            TryUpdateAttribute("minecraft:generic.attack_damage", Size + 2);
        }
    }
    protected override string? SoundName => "magma_cube";
    protected override bool TakesFallDamage => false;
    protected override bool CanDamageOnContact => true;
    internal override int JumpDelayMultiplier => 4;
    internal override float JumpPower => 0.42f + Size * 0.1f;
    protected override Slime CreateSplitChild() => new MagmaCube { Level = Level, MobBitMask = MobBitMask };
    protected override void DropSmallLoot() { }
    protected override async ValueTask OnDeathAsync(IEntity source)
    {
        if (Size > 1)
            DropItem(Material.MagmaCream, Random.Next(2));
        await base.OnDeathAsync(source);
    }
    protected override VectorF Travel()
    {
        if (InLava && JumpControl.Consume())
            Motion = new VectorF(Motion.X, 0.22f + Size * 0.05f, Motion.Z);
        return base.Travel();
    }
}
