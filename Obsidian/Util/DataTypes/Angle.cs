using System;

namespace Obsidian.Util.DataTypes
{
    /// <summary>
    /// A class that represents an angle from 0° to 360° degrees.
    /// </summary>
    public struct Angle
    {
        public byte Value { get; set; }
        public float Degrees
        {
            get => Value * 360f / 256f;
            set => Value = (byte)(NormalizeDegree(value) * 256f / 360f);
        }

        public Angle(byte value)
        {
            //if (value < MinValue || value > MaxValue)
            // throw new ArgumentOutOfRangeException(nameof(value), $"Argument needs to be a valid angle, provided was {value}");// -0.15000002

            this.Value = value;
        }

        internal static float NormalizeDegree(float degree)
        {
            degree %= 360;
            if (degree < 0)
                degree += 360;
            return degree;
        }

        internal static Angle FromDegrees(float value) => new Angle((byte)(NormalizeDegree(value) * 256f / 360f));
    }
}

