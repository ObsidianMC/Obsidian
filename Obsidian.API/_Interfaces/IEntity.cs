﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IEntity
    {
        public IWorld WorldLocation { get; }
        public VectorF Position { get; set; }
        public Angle Pitch { get; set; }
        public Angle Yaw { get; set; }
        public int EntityId { get; }
        public EntityBitMask EntityBitMask { get; set; }
        public Pose Pose { get; set; }
        public EntityType Type { get; set; }
        public int Air { get; set; }
        public bool CustomNameVisible { get; }
        public bool Silent { get; }
        public bool NoGravity { get; set; }
        public bool OnGround { get; set; }

        public Task RemoveAsync();
        public Task TickAsync();

        public IEnumerable<IEntity> GetEntitiesNear(float distance);
    }
}
