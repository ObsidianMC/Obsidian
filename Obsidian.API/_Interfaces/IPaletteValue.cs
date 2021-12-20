﻿using System.Runtime.Versioning;

namespace Obsidian.API;

[RequiresPreviewFeatures]
public interface IPaletteValue<TSelf> where TSelf : IPaletteValue<TSelf>
{
    public static abstract TSelf Construct(int value);
}
