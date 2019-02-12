using Obsidian.Concurrency;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Obsidian.Entities
{
    public class MinecraftPlayer
    {
        public string Username { get; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public bool OnGround { get; set; }

        public ConcurrentHashSet<string> Permissions { get; }

        public MinecraftPlayer(string username, double x, double y, double z)
        {
            this.Username = username;
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.Permissions = new ConcurrentHashSet<string>();
        }

        public void LoadPerms(List<string> permissions)
        {
            foreach(var perm in permissions)
            {
                Permissions.Add(perm);
            }
        }
    }
}
