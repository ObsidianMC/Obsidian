using System;

namespace Obsidian.API
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class CommandRootAttribute : Attribute
    {
    }
}
