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

        public const string BoolType = "bool";
        public const string IntType = "int";

        public Property(string name, string tag, string type, string[] values, int? customOffset = null)
        {
            Name = name;
            Tag = tag;
            Type = type;
            Values = values;
            CustomOffset = customOffset;
            IsEnum = type != BoolType && type != IntType;
        }

        public string GetValue(ref int offset)
        {
            string value = Type switch
            {
                BoolType => $"({Name} ? 0 : {offset})",
                IntType => $"({Name} * {offset})",
                _ => $"((int){Name} * {offset})",
            };

            offset *= Values.Length;
            return value;
        }
    }
}
