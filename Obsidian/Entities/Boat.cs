namespace Obsidian.Entities;

[MinecraftEntity("minecraft:boat")]
public sealed partial class Boat : Entity
{
    public int LastTimeHit { get; private set; }
    public int ForwardDirection { get; private set; }

    public float DamageTaken { get; private set; }

    public BoatType BoatType { get; private set; }

    public bool LeftPadleTurning { get; private set; }
    public bool RightPaddleTurning { get; private set; }

    public int SplashTimer { get; private set; }
}

public enum BoatType : byte
{
    Oak,
    Spruce,
    Birch,
    Jungle,
    Acacia,
    DarkOak
}
