using System.Collections.Generic;

namespace Obsidian.Util
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
