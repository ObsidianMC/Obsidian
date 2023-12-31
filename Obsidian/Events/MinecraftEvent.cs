﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Obsidian.Plugins;

namespace Obsidian.Events;
internal readonly struct MinecraftEvent
{
    public required Type EventType { get; init; }

    public Type? ModuleType { get; init; }

    public required PluginContainer PluginContainer { get; init; }

    public required Priority Priority { get; init; }

    public ObjectFactory? ModuleFactory { get; init; }

    public ObjectMethodExecutor? MethodExecutor { get; init; }

    public Delegate? MethodDelegate { get; init; }
}
