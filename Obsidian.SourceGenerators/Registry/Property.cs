namespace Obsidian.SourceGenerators.Registry
{
    internal sealed class Property
    {
        public string Name { get; }
        public string Tag { get; }
        public string Type { get; }
        public string[] Values { get; }
        public bool IsEnum { get; }

        public static readonly string BoolType = "bool";
        public static readonly string IntType = "int";

        public Property(string name, string tag, string type, string[] values)
        {
            Name = name;
            Tag = tag;
            Type = type;
            Values = values;
            IsEnum = type != BoolType && type != IntType;
        }
    }
}
