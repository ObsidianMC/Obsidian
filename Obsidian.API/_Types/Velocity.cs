using System;

namespace Obsidian.API
{
    /// <summary>
    /// Represents velocity of an entity in the world.
    /// </summary>
    public struct Velocity
    {
        /// <summary>
        /// Velocity on the X axis.
        /// </summary>
        public short X { get; set; }
        /// <summary>
        /// Velocity on the Y axis.
        /// </summary>
        public short Y { get; set; }
        /// <summary>
        /// Velocity on the Z axis.
        /// </summary>
        public short Z { get; set; }

        /// <summary>
        /// Returns the length of this <see cref="Velocity"/>.
        /// </summary>
        public float Magnitude => MathF.Sqrt(X * X + Y + Y + Z * Z);

        /// <summary>
        /// Creates a new instance of <see cref="Velocity"/> with specific values.
        /// </summary>
        /// <param name="x">Velocity on the X axis.</param>
        /// <param name="y">Velocity on the Y axis.</param>
        /// <param name="z">Velocity on the Z axis.</param>
        public Velocity(short x, short y, short z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Returns <see cref="Velocity"/> expressed as how many blocks on each axis can be travelled in a second.
        /// </summary>
        /// <param name="x">How many blocks can be travelled on the X axis in a second.</param>
        /// <param name="y">How many blocks can be travelled on the Y axis in a second.</param>
        /// <param name="z">How many blocks can be travelled on the Z axis in a second.</param>
        public static Velocity FromBlockPerSecond(float x, float y, float z)
        {
            return new Velocity((short)(400f * x), (short)(400f * y), (short)(400f * z));
        }

        /// <summary>
        /// Returns <see cref="Velocity"/> expressed as how many blocks on each axis can be travelled in a tick (50ms).
        /// </summary>
        /// <param name="x">How many blocks can be travelled on the X axis in a tick (50ms).</param>
        /// <param name="y">How many blocks can be travelled on the Y axis in a tick (50ms).</param>
        /// <param name="z">How many blocks can be travelled on the Z axis in a tick (50ms).</param>
        public static Velocity FromBlockPerTick(float x, float y, float z)
        {
            return new Velocity((short)(8000f * x), (short)(8000f * y), (short)(8000f * z));
        }

        /// <summary>
        /// Turns <see cref="Position"/> into <see cref="Velocity"/>, using it's coordinates as to how many blocks can be travelled per second.
        /// </summary>
        /// <param name="position"><see cref="Position"/> to be used for conversion.</param>
        public static Velocity FromPosition(Position position)
        {
            return FromBlockPerSecond(position.X, position.Y, position.Z);
        }

        /// <summary>
        /// Turns <see cref="PositionF"/> into <see cref="Velocity"/>, using it's coordinates as to how many blocks can be travelled per second.
        /// </summary>
        /// <param name="position"><see cref="PositionF"/> to be used for conversion.</param>
        public static Velocity FromPosition(PositionF position)
        {
            return FromBlockPerSecond(position.X, position.Y, position.Z);
        }

        /// <summary>
        /// Returns such velocity, that can travel from <paramref name="from"/> to <paramref name="to"/> in a second.
        /// </summary>
        /// <param name="from">Starting position.</param>
        /// <param name="to">Target position.</param>
        public static Velocity FromDirection(Position from, Position to)
        {
            return FromPosition(to - from);
        }

        /// <summary>
        /// Returns such velocity, that can travel from <paramref name="from"/> to <paramref name="to"/> in a second.
        /// </summary>
        /// <param name="from">Starting position.</param>
        /// <param name="to">Target position.</param>
        public static Velocity FromDirection(PositionF from, PositionF to)
        {
            return FromPosition(to - from);
        }
    }
}