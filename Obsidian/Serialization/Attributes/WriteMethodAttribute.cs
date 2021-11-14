using System;

namespace Obsidian.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WriteMethodAttribute : Attribute
{
}
