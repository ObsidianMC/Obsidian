namespace Obsidian.API;

/// <summary>
/// A class that represents an angle from 0° to 360° degrees.
/// </summary>
public struct Angle(byte value) : INetworkSerializable<Angle>
{
    public byte Value { get; set; } = value;
    public float Degrees
    {
        get => Value * 360f / 256f;
        set => Value = NormalizeToByte(value);
    }

    public static implicit operator Angle(float degree) => new(NormalizeToByte(ClampDegrees(degree)));

    public static implicit operator float(Angle angle) => angle.Degrees;

    public static byte NormalizeToByte(float value) => (byte)(value * 256f / 360f);

    private static float ClampDegrees(float degrees)
    {
        float clamped = degrees % 360f;
        return clamped < 0f ? clamped + 360f : clamped;
    }

    public static void Write(Angle value, INetStreamWriter writer) => writer.WriteSingle(value);
    public static Angle Read(INetStreamReader reader) => reader.ReadSingle();
}
