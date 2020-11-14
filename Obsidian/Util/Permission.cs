using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
