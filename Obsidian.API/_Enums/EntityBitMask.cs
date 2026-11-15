namespace Obsidian.API;

/// <summary>The state flags every entity sends in its first metadata byte.</summary>
[Flags]
public enum EntityBitMask : byte
{
    /// <summary>No flags set.</summary>
    None = 0x00,
    /// <summary>The entity is on fire.</summary>
    OnFire = 0x01,
    /// <summary>The entity is crouching.</summary>
    Crouched = 0x02,

    /// <summary>No longer used by the client.</summary>
    [Obsolete]
    Riding = 0x04,

    /// <summary>The entity is sprinting.</summary>
    Sprinting = 0x08,
    /// <summary>The entity is swimming.</summary>
    Swimming = 0x10,
    /// <summary>The entity is invisible.</summary>
    Invisible = 0x20,
    /// <summary>The entity has the glowing outline.</summary>
    Glowing = 0x40,
    /// <summary>The entity is gliding with an elytra.</summary>
    FlyingWithElytra = 0x80
}
