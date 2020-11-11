using Obsidian.CommandFramework;
using Obsidian.CommandFramework.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class RequirePermissionAttribute : BaseExecutionCheckAttribute
    {
        private string[] permissions;
        private bool op = true;

        public RequirePermissionAttribute(bool op = false, params string[] permissions)
        {
            this.permissions = permissions;
            this.op = op;
        }

        public override async Task<bool> RunChecksAsync(ObsidianContext ctx)
        {
            if (this.op && ctx.Player.IsOperator)
                return true;

            if (this.permissions != null)
                return await ctx.Player.HasAnyPermission(permissions);

            return false;
        }
    }
}