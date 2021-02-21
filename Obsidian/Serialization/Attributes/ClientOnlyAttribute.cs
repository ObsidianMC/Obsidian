using System;

namespace Obsidian.Serialization.Attributes
{
    /// <summary>
    /// Marks a packet as write-only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public class ClientOnlyAttribute : Attribute
    {
    }
}
