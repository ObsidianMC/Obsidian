using System;

namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class RemainingAttribute : Attribute
{
}
