using Obsidian.API;
using Obsidian.Commands.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Commands.Framework.Entities
{
    public interface ICommand
    {
        Task ExecuteAsync(CommandContext context, string[] args);
    }
}
