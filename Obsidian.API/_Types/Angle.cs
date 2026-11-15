namespace Obsidian.API;

/// <summary>
/// An angle packed into one byte, in 256ths of a turn, as the protocol sends entity rotations in spawn, move, look and
/// head rotation packets.
/// </summary>
/// <remarks>
/// Packing loses up to 1.4 degrees, so entities keep their rotation as float degrees (<see cref="IEntity.Yaw"/>,
/// <see cref="IEntity.Pitch"/>) and pack it only to send it.
/// </remarks>
public struct Angle(byte value) : INetworkSerializable<Angle>, IEquatable<Angle>
{
    /// <summary>Gets or sets the angle in 256ths of a turn.</summary>
    public byte Value { get; set; } = value;
    /// <summary>
    /// Gets or sets the angle in degrees. Setting it packs the value as <see cref="NormalizeToByte"/> does.
    /// </summary>
    public float Degrees
    {
        get => Value * 360f / 256f;
        set => Value = NormalizeToByte(value);
    }

    /// <summary>
    /// Packs <paramref name="degree"/> as <see cref="NormalizeToByte"/> does.
    /// </summary>
    public static implicit operator Angle(float degree) => new(NormalizeToByte(degree));

    /// <summary>Converts the angle to degrees.</summary>
    public static implicit operator float(Angle angle) => angle.Degrees;

    /// <summary>Indicates whether two angles pack to the same byte.</summary>
    public static bool operator ==(Angle left, Angle right) => left.Equals(right);

    /// <summary>Indicates whether two angles pack to different bytes.</summary>
    public static bool operator !=(Angle left, Angle right) => !left.Equals(right);

    /// <summary>Indicates whether this angle packs to the same byte as <paramref name="other"/>.</summary>
    public readonly bool Equals(Angle other) => this.Value == other.Value;

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is Angle angle && this.Equals(angle);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => this.Value.GetHashCode();

    /// <summary>
    /// Packs <paramref name="value"/> degrees into 256ths of a turn, as vanilla's <c>Mth.packDegrees</c> does: floored,
    /// then wrapped into a byte, so -90 is 192 and 360 is 0.
    /// </summary>
    /// <remarks>
    /// The value narrows through <see cref="int"/> to keep the wrap: since .NET 11, a float cast straight to a byte
    /// saturates instead, which would turn every negative angle into 0.
    /// </remarks>
    public static byte NormalizeToByte(float value) => (byte)(int)MathF.Floor(value * 256f / 360f);

    /// <summary>Writes the angle to a network stream as a float of degrees.</summary>
    public static void Write(Angle value, INetStreamWriter writer) => writer.WriteSingle(value);
    /// <summary>Reads an angle from a network stream, given as a float of degrees.</summary>
    public static Angle Read(INetStreamReader reader) => reader.ReadSingle();
}
