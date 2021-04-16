using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API
{
    [Flags]
    public enum CommandIssuers
    {
        None,
        Client,
        Console,
        RemoteConsole,
        Plugin,
        Any = ~None

    }
}
