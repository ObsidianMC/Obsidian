namespace Obsidian.SourceGenerators.Packets;

internal static class Vocabulary
{
    public const string ClientboundInterface = "ClientboundPacket";
    public const string ServerboundInterface = "ServerboundPacket";
    public const string WriteMethodAttribute = "WriteMethod";
    public const string ReadMethodAttribute = "ReadMethod";
    public const string FieldAttribute = "Field";
    public const string ActualTypeAttribute = "ActualType";
    public const string CountTypeAttribute = "CountType";
    public const string FixedLengthAttribute = "FixedLength";
    public const string VarLengthAttribute = "VarLength";
    public const string DataFormatAttribute = "DataFormat";
    public const string ConditionAttribute = "Condition";

    public const string Clientbound = "Clientbound";
    public const string Serverbound = "Serverbound";
    public const string PacketId = "PacketId";

    public const string ProtocolId = "protocol_id";

    public static bool AttributeNamesEqual(string attributeName1, string attributeName2)
    {
        ReadOnlySpan<char> name1 = attributeName1.EndsWith("Attribute") ? attributeName1.AsSpan(0, attributeName1.Length - 9) : attributeName1.AsSpan();
        ReadOnlySpan<char> name2 = attributeName2.EndsWith("Attribute") ? attributeName2.AsSpan(0, attributeName2.Length - 9) : attributeName2.AsSpan();
        return MemoryExtensions.Equals(name1, name2, StringComparison.Ordinal);
    }
}
