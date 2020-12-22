using System;

namespace Obsidian.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class VarLengthAttribute : Attribute
    {
    }
}
