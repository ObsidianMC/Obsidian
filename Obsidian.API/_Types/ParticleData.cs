namespace Obsidian.API;

public sealed class ParticleData
{
    public static readonly ParticleData None = new();

    private object data;
    internal ParticleType ParticleType { get; set; }

#nullable disable
    private ParticleData()
    {
        data = null;
        ParticleType = (ParticleType)(-1);
    }
#nullable restore

    private ParticleData(object data, ParticleType targetType)
    {
        this.data = data;
        ParticleType = targetType;
    }

    internal T GetDataAs<T>()
    {
        if (data is T t)
        {
            return t;
        }
        throw new InvalidOperationException();
    }

    public static ParticleData ForBlock(int blockState) => new(blockState, ParticleType.Block);
    public static ParticleData ForDust(float red, float green, float blue, float scale) => new((red, green, blue, scale), ParticleType.Dust);
    public static ParticleData ForFallingDust(int blockState) => new(blockState, ParticleType.FallingDust);
    public static ParticleData ForItem(ItemStack item) => new(item, ParticleType.Item);
}
