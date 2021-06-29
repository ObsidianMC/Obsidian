namespace Obsidian.SourceGenerators.Registry
{
    internal sealed class Property
    {
        public string Name { get; }
        public string Tag { get; }
        public string Type { get; }
        public string[] Values { get; }
        public bool IsEnum { get; }
        public int? CustomOffset { get; }
        public bool IsBooleanFlipped { get; }

        public const string BoolType = "bool";
        public const string IntType = "int";

        public Property(string name, string tag, string type, string[] values, int? customOffset = null, bool isBooleanFlipped = true)
        {
            Name = name;
            Tag = tag;
            Type = type;
            Values = values;
            CustomOffset = customOffset;
            IsBooleanFlipped = isBooleanFlipped;
            IsEnum = type != BoolType && type != IntType;
        }

        public string GetValue(ref int offset)
        {
            int multiplier = CustomOffset ?? offset;
            offset *= Values.Length;
            return Type switch
            {
                BoolType when IsBooleanFlipped => $"({Name} ? 0 : {multiplier})",
                BoolType when !IsBooleanFlipped => $"({Name} ? {multiplier} : 0)",
                IntType => $"({Name} * {multiplier})",
                _ => $"((int){Name} * {multiplier})",
            };
        }
    }
}
