using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Indicates that a property should be injected with a service from <see cref="API.Services"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class InjectAttribute : Attribute
    {
    }
}
