using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetRecipeBookState : IServerboundPacket
{
    [Field(0), VarLength, ActualType(typeof(int))]
    public RecipeBookType BookId { get; private set; }

    [Field(1)]
    public bool BookOpen { get; private set; }
    
    [Field(2)]
    public bool FilterActive { get; private set; }

    public int Id => 0x1E;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;

}
