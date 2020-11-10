using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.API.Events
{
    public class PermissionRevokedEventArgs : PlayerEventArgs
    {
        public string Permission { get; }

        public PermissionRevokedEventArgs(IPlayer player, string permission) : base(player)
        {
            Permission = permission;
        }
    }
}
