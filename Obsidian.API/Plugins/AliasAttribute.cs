using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Specifies the property/field name that is used for dependency injection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class AliasAttribute : Attribute
    {
        /// <summary>
        /// Name that is used for dependency injection.
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="AliasAttribute"/> with the specified identifier.
        /// </summary>
        /// <param name="identifier">Name that is used for dependency injection.</param>
        public AliasAttribute(string identifier)
        {
            Identifier = identifier;
        }
    }
}
