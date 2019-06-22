// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Obsidian.Concurrency;
using Obsidian.PlayerData;
using Obsidian.Util;
using System;
using System.Collections.Generic;

namespace Obsidian.Entities
{
    public class Player : Living
    {
        // ETC
        public Guid UUID { get; set; }

        // Properties set by Minecraft (official)
        public Transform Transform { get; set; }

        public bool OnGround { get; set; }
        public bool Sleeping { get; set; }

        public short Air { get; set; }
        public short AttackTime { get; set; }
        public short DeathTime { get; set; }
        public short Fire { get; set; }
        public short HurtTime { get; set; }
        public short SleepTimer { get; set; }

        public int Dimension { get; set; }
        public int FoodLevel { get; set; }
        public int FoodTickTimer { get; set; }
        public Gamemode Gamemode { get; set; }
        public int XpLevel { get; set; }
        public int XpTotal { get; set; }

        public float FallDistance { get; set; }
        public float FoodExhastionLevel { get; set; } // not a type, it's in docs like this
        public float FoodSaturationLevel { get; set; }
        public float XpP { get; set; } // idfk, xp points?

        public object MainHand { get; set; }

        public Entity LeftShoulder { get; set; }
        public Entity RightShoulder { get; set; }

        /* Missing for now:
            NbtCompound(inventory)
            NbtList(Motion)
            NbtList(Pos)
            NbtList(Rotation)
        */

        // Properties set by Obsidian (unofficial)
        // Not sure whether these should be saved to the NBT file.
        // These could be saved under nbt tags prefixed with "obsidian_"
        // As minecraft might just ignore them.
        public ConcurrentHashSet<string> Permissions { get; }
        public string Username { get; }
        public World World;

        public Player(Guid uuid, string username)
        {
            this.UUID = uuid;
            this.Username = username;
            this.Permissions = new ConcurrentHashSet<string>();
            this.Transform = new Transform();
        }

        public void UpdatePosition(Position pos, bool? onGround = null)
        {
            this.Transform.X = pos.X;
            this.Transform.Y = pos.Y;
            this.Transform.Z = pos.Z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(Transform pos, bool? onGround = null)
        {
            this.Transform.X = pos.X;
            this.Transform.Y = pos.Y;
            this.Transform.Z = pos.Z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(double x, double y, double z, bool? onGround = null)
        {
            this.Transform.X = x;
            this.Transform.Y = y;
            this.Transform.Z = z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(float pitch, float yaw, bool? onGround = null)
        {
            this.Transform.Pitch = pitch;
            this.Transform.Yaw = yaw;
            this.OnGround = onGround ?? this.OnGround;
        }
        public void LoadPerms(List<string> permissions)
        {
            foreach (var perm in permissions)
            {
                Permissions.Add(perm);
            }
        }
    }
}
