using Obsidian.CommandFramework;
using Obsidian.CommandFramework.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class RequirePermissionAttribute : BaseExecutionCheckAttribute
    {
        private string[] permissions;
        private PermissionCheckType checkType;
        private bool op;

        public RequirePermissionAttribute(PermissionCheckType checkType = PermissionCheckType.All, bool op = true, params string[] permissions)
        {
            this.permissions = permissions;
            this.checkType = checkType;
            this.op = op;
        }

        public override async Task<bool> RunChecksAsync(ObsidianContext ctx)
        {
            if (this.op && ctx.Player.IsOperator)
                return true;

            if (this.permissions.Length > 0)
                return checkType == PermissionCheckType.All ? await ctx.Player.HasAllPermissions(permissions) : await ctx.Player.HasAnyPermission(permissions);

            return false;
        }
    }
}