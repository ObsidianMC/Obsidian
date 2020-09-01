using System;

namespace Obsidian.Util.DataTypes
{
    /// <summary>
    /// A class that represents an angle from 0° to 360° degrees.
    /// </summary>
    public struct Angle
    {
        public const float MinValue = 0;
        public const float MaxValue = 360;

        private float _value;

        public Angle(float value)
        {
            //if (value < MinValue || value > MaxValue)
               // throw new ArgumentOutOfRangeException(nameof(value), $"Argument needs to be a valid angle, provided was {value}");// -0.15000002

            _value = value;
        }

        public static explicit operator Angle(float degree) => new Angle(NormalizeDegree(degree));

        public static implicit operator float(Angle angle) => angle._value;

        private static float NormalizeDegree(float degree)
        {
            degree %= 360;
            if (degree < 0) degree += 360;
            return degree;
        }
    }
}
