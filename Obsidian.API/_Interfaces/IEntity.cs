using Obsidian.API.AI;

namespace Obsidian.API;

public interface IEntity
{
    public IServer Server { get; }

    public IWorld WorldLocation { get; }
    public INavigator Navigator { get; set; }

    public IGoalController GoalController { get; set; }

    public Guid Uuid { get; set; }

    public VectorF Position { get; set; }
    public Angle Pitch { get; set; }
    public Angle Yaw { get; set; }
    public int EntityId { get; }

    public Pose Pose { get; set; }
    public EntityType Type { get; }

    public int Air { get; set; }

    public float Health { get; set; }

    public ChatMessage CustomName { get; set; }

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
    public Task DamageAsync(IEntity source, float amount = 1.0f);

    public Task KillAsync(IEntity source);
    public Task KillAsync(IEntity source, ChatMessage message);

    public IEnumerable<IEntity> GetEntitiesNear(float distance);

    public VectorF GetLookDirection();
}
