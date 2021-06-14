using System;

namespace Obsidian.SourceGenerators.Packets.Attributes
{
    [Flags]
    internal enum AttributeFlags
    {
        None = 0,
        Field = 1,
        Condition = 2,
        ActualType = 4,
        CountType = 8,
        FixedLength = 16,
        VarLength = 32,
        VectorFormat = 64
    }
}
