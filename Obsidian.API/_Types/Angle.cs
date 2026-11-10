namespace Obsidian.API;

/// <summary>
/// A class that represents an angle from 0° to 360° degrees.
/// </summary>
public struct Angle(byte value) : INetworkSerializable<Angle>, IEquatable<Angle>
{
    public byte Value { get; set; } = value;
    public float Degrees
    {
        get => Value * 360f / 256f;
        set => Value = NormalizeToByte(value);
    }

    public static implicit operator Angle(float degree) => new(NormalizeToByte(ClampDegrees(degree)));

    public static implicit operator float(Angle angle) => angle.Degrees;

    public static bool operator ==(Angle left, Angle right) => left.Equals(right);

    public static bool operator !=(Angle left, Angle right) => !left.Equals(right);

    public readonly bool Equals(Angle other) => this.Value == other.Value;

    public readonly override bool Equals(object? obj) => obj is Angle angle && this.Equals(angle);

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

    private static float ClampDegrees(float degrees)
    {
        float clamped = degrees % 360f;
        return clamped < 0f ? clamped + 360f : clamped;
    }

    public static void Write(Angle value, INetStreamWriter writer) => writer.WriteSingle(value);
    public static Angle Read(INetStreamReader reader) => reader.ReadSingle();
}
