using System;
using System.Threading.Tasks;

namespace Obsidian.API
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequirePermissionAttribute : BaseExecutionCheckAttribute
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

        public override async Task<bool> RunChecksAsync(CommandContext context)
        {
            if (this.op && context.Player.IsOperator)
                return true;

            if (this.permissions.Length > 0)
                return checkType == PermissionCheckType.All ? await context.Player.HasAllPermissions(permissions) : await context.Player.HasAnyPermission(permissions);

            return false;
        }
    }
}
