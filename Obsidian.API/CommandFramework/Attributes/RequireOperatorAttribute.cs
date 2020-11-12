using Obsidian.CommandFramework;
using Obsidian.CommandFramework.Attributes;
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

        public override async Task<bool> RunChecksAsync(ObsidianContext ctx)
        {
            if (this.op && ctx.Player.IsOperator)
                return true;

            if (this.permission != "")
                return await ctx.Player.HasPermission(permission.ToLower());

            return false;
        }
    }
}