using System;

namespace Obsidian.API.Plugins
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class AliasAttribute : Attribute
    {
        public string Identifier { get; }

        public AliasAttribute(string identifier)
        {
            Identifier = identifier;
        }
    }
}
