namespace Obsidian.API;

/// <summary>The body pose of an entity, sent in its metadata.</summary>
public enum Pose : int
{
    /// <summary>Standing normally.</summary>
    Standing,

    /// <summary>Gliding with an elytra.</summary>
    FallFlying,

    /// <summary>Lying in a bed.</summary>
    Sleeping,

    /// <summary>Swimming.</summary>
    Swimming,

    /// <summary>Spinning from a riptide trident.</summary>
    SpinAttack,

    /// <summary>Crouching.</summary>
    Sneaking,

    /// <summary>A goat's ramming leap.</summary>
    LongJumper,
    
    /// <summary>Dying.</summary>
    Dying,

    /// <summary>A frog's croak.</summary>
    Croaking,

    /// <summary>A frog using its tongue.</summary>
    UsingTongue,

    /// <summary>Sitting, such as a camel.</summary>
    Sitting,

    /// <summary>A warden's roar.</summary>
    Roaring,

    /// <summary>A warden or sniffer sniffing.</summary>
    Sniffing,

    /// <summary>A warden climbing out of the ground.</summary>
    Emerging,

    /// <summary>A warden or sniffer digging.</summary>
    Digging,

    /// <summary>A breeze sliding.</summary>
    Sliding,

    /// <summary>A breeze shooting a wind charge.</summary>
    Shooting,
    
    /// <summary>A breeze breathing in.</summary>
    Inhaling
}
