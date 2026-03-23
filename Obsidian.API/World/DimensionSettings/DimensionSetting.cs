using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.API.World.DimensionSettings;

public sealed record class DimensionSetting
{
    public string Type { get; set; }

    public DimensionGeneratorSettings Generator { get; set; }
}
