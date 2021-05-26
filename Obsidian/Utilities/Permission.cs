using System.Collections.Generic;

namespace Obsidian.Utilities
{
    public class Permission
    {
        public string Name { get; set; }

        public List<Permission> Children { get; set; }

        public Permission(string name)
        {
            this.Name = name;
            this.Children = new List<Permission>();
        }
    }
}
