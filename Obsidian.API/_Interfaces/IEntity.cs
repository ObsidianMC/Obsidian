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

        public Pose Pose { get; set; }
        public int Air { get; set; }

        public bool CustomNameVisible { get; }
        public bool Silent { get; }
        public bool NoGravity { get; }
        public bool OnGround { get; }
        public bool Sneaking { get; }
        public bool Sprinting { get; }
        public bool Glowing { get; }
        public bool Invisible { get; }
        public bool Burning { get; }
        public bool Swimming { get; }
        public bool FlyingWithElytra { get; }

        public Task RemoveAsync();
        public Task TickAsync();

        public VectorF GetLookDirection();
    }
}
