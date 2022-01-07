using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData;

internal class WorldGeneratorAttribute : Attribute
{
    public string Id { get; private set; }

    public WorldGeneratorAttribute(string id)
    {
        Id = id;
    }
}
