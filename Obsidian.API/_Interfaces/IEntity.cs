﻿using Obsidian.API.AI;

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
    public bool Summonable { get; }
    public bool IsFireImmune { get; }

    public Task RemoveAsync();
    public Task TickAsync();
    public Task DamageAsync(IEntity source, float amount = 1.0f);

    public Task KillAsync(IEntity source);
    public Task KillAsync(IEntity source, ChatMessage message);

    public Task TeleportAsync(IWorld world);
    public Task TeleportAsync(IEntity to);
    public Task TeleportAsync(VectorF pos);

    public IEnumerable<IEntity> GetEntitiesNear(float distance);

    public VectorF GetLookDirection();

    public bool HasAttribute(string attributeResourceName);
    public bool TryAddAttribute(string attributeResourceName, float value);
    public bool TryUpdateAttribute(string attributeResourceName, float newValue);

    public float GetAttributeValue(string attributeResourceName);
}
