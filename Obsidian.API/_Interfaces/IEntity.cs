using Obsidian.API.AI;

namespace Obsidian.API;

public interface IEntity
{
    public IWorld World { get; }
    public INavigator? Navigator { get; set; }

    public IGoalController? GoalController { get; set; }

    public Guid Uuid { get; set; }

    public VectorF Position { get; set; }
    public Angle Pitch { get; set; }
    public Angle Yaw { get; set; }

    public int EntityId { get; }

    public Pose Pose { get; set; }
    public EntityType Type { get; }

    public BoundingBox BoundingBox { get; }
    public EntityDimension Dimension { get; }
    public int Air { get; set; }

    public float Health { get; set; }

    public ChatMessage? CustomName { get; set; }

    public string? TranslationKey { get; }

    public bool CustomNameVisible { get; set; }
    public bool Silent { get; }
    public bool NoGravity { get; }
    public MovementFlags MovementFlags { get; }
    public bool Sneaking { get; }
    public bool Sprinting { get; }
    public bool Glowing { get; }
    public bool Invisible { get; }
    public bool Burning { get; }
    public bool Swimming { get; }
    public bool FlyingWithElytra { get; }
    public bool Summonable { get; }
    public bool IsFireImmune { get; }

    public ValueTask RemoveAsync();
    public ValueTask TickAsync();
    public ValueTask DamageAsync(IEntity source, float amount = 1.0f);

    public ValueTask KillAsync(IEntity source);
    public ValueTask KillAsync(IEntity source, ChatMessage message);

    public ValueTask TeleportAsync(IWorld world);
    public ValueTask TeleportAsync(IEntity to);
    public ValueTask TeleportAsync(VectorF pos);

    public void Write(INetStreamWriter writer);

    public bool IsInRange(IEntity entity, float distance);

    /// <summary>
    /// Spawns the specified entity to player nearby in the world.
    /// </summary>
    /// <param name="velocity">The velocity the entity should spawn with</param>
    /// <param name="additionalData">Additional data for the entity. More info here: <see href="https://wiki.vg/Object_Data"/></param>
    public void SpawnEntity(Velocity? velocity = null, int additionalData = 0);

    public IEnumerable<IEntity> GetEntitiesNear(float distance);

    public VectorF GetLookDirection();

    public void SetHeadRotation(Angle headYaw);
    public void SetRotation(Angle yaw, Angle pitch, MovementFlags movementFlags);

    public bool HasAttribute(string attributeResourceName);
    public bool TryAddAttribute(string attributeResourceName, float value);
    public bool TryUpdateAttribute(string attributeResourceName, float newValue);

    public float GetAttributeValue(string attributeResourceName);
}
