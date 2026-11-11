using Obsidian.Nbt;

namespace Obsidian.Entities;

public class AgeableMob : PathfinderMob, IAgeable, IAgeLockable
{
    // Base-layout counts keep inherited offsets aligned without a mutable cursor during concurrent writes.
    protected const byte AgeableMetadataCount = MobMetadataCount + 2;

    // Vanilla's BABY_START_AGE: how young a baby starts, in ticks to growing up.
    internal const int BabyStartAge = -24000;

    private bool legacyBaby;

    public int Age { get; internal set; }

    /// <summary>
    /// Vanilla's <c>forcedAge</c>: the growth feeding forced on a baby, which it keeps as its age once it's grown, and
    /// saves as <c>ForcedAge</c>.
    /// </summary>
    internal int ForcedAge { get; set; }

    /// <summary>
    /// Whether a golden dandelion paused the mob's growing up (vanilla's <c>AgeLocked</c>), which clients are told.
    /// </summary>
    public bool AgeLocked { get; set; }

    public virtual bool IsBaby
    {
        get => HasAi ? Age < 0 : legacyBaby;
        set
        {
            legacyBaby = value;
            Age = value ? BabyStartAge : 0;
        }
    }

    bool IAgeLockable.CanLockAge => IsBaby;

    int IAgeLockable.AgeLockTimer { get; set; }

    protected override float DimensionScale => HasAi && IsBaby ? 0.5f : 1;

    /// <summary>
    /// Vanilla's <c>canAgeUp</c>: a baby grows up unless a golden dandelion locked its age.
    /// </summary>
    internal bool CanAgeUp => IsBaby && !AgeLocked;

    void IAgeLockable.ToggleAgeLock()
    {
        AgeLocked = !AgeLocked;
        Age = BabyStartAge;
        ((IAgeLockable)this).AgeLockTimer = AgeLocks.ParticleTicks;
        SynchronizeMetadata();
    }

    /// <summary>
    /// Vanilla's <c>ageUp</c> with <c>forced</c>: <paramref name="ticks"/> more growth, short of growing up past 0; the
    /// growth is remembered as forced, and a baby that grew up takes the forced growth as its age.
    /// </summary>
    internal void ForceGrowth(int ticks)
    {
        var before = Age;
        Age = Math.Min(0, Age + ticks);
        ForcedAge += Age - before;
        if (Age == 0)
            Age = ForcedAge;
    }

    protected override ValueTask TickMobAsync()
    {
        // Vanilla's AgeableMob.aiStep: a baby grows unless its age is locked; an adult's breeding cooldown runs out.
        var wasBaby = IsBaby;
        if (CanAgeUp)
            Age++;
        else if (Age > 0)
            Age--;

        if (wasBaby != IsBaby)
            SynchronizeMetadata();

        AgeLocks.Tick(this, this);
        return base.TickMobAsync();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(MobMetadataCount, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);

        writer.WriteEntityMetadataType(MobMetadataCount + 1, EntityMetadataType.Boolean);
        writer.WriteBoolean(AgeLocked);
    }

    // Vanilla's addAdditionalSaveData for mobs that can be babies: Age, ForcedAge and AgeLocked.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<int>("Age", HasAi ? Age : this.IsBaby ? BabyStartAge : 0));
        tag.Set(new NbtTag<int>("ForcedAge", ForcedAge));
        tag.Set(new NbtTag<bool>("AgeLocked", AgeLocked));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (HasAi)
            Age = tag.TryGetNumber<int>("Age", out var age) ? age : 0;
        else
            this.IsBaby = tag.TryGetNumber<int>("Age", out var legacyAge) && legacyAge < 0;

        ForcedAge = tag.TryGetNumber<int>("ForcedAge", out var forced) ? forced : 0;
        AgeLocked = tag.TryGetBool("AgeLocked", out var locked) && locked;
    }
}
