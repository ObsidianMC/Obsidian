using Obsidian.API;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class RequirePermissionAttribute : BaseExecutionCheckAttribute
    {
        string permission = "";
        bool op = true;

        public RequirePermissionAttribute(string permission = "", bool op = true)
        {

        }

        public override async Task<bool> RunChecksAsync(BaseCommandContext ctx)
        {
            if (ctx is ObsidianContext c)
            {
                if(this.op && c.Player.IsOperator)
                    return true;

                if (this.permission != "")
                    return await c.Player.HasPermission(permission);
            }

            throw new Exception($"This context ({ctx.ToString()}) is unsupported");
        }
    }
}