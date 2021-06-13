using System;

namespace Obsidian.SourceGenerators.Packets.Attributes
{
    [Flags]
    internal enum AttributeFlags
    {
        None = 0,
        Field = 1,
        ActualType = 2,
        CountType = 4,
        FixedLength = 8,
        VarLength = 16,
        VectorFormat = 32
    }
}
