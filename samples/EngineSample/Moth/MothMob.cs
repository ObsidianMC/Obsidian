using Obsidian.API;
using Obsidian.API.Client;
using Obsidian.API.Data;
using Obsidian.API.Entities;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.Nbt;

namespace EngineSample.Moth;

/// <summary>
/// Phase 4's mob, <c>sample:moth</c>: a big moth that flies, which players ride by using it, and whose eyes glow in
/// the dark on the Obsidian client. Ridden, it flies where its rider looks (see <c>EntityMovementMode.Flying</c>);
/// sneaking gets off. Phase 5 has it count its rides in a saved attachment, <see cref="Rides"/>, and drop a charm
/// when a player kills it, through its loot table, <c>data/sample/loot_table/entities/moth.json</c>.
/// </summary>
/// <remarks>
/// Its model and textures are <c>assets/sample/models/entity/moth.json</c> and
/// <c>assets/sample/textures/entity/moth.png</c>, and its eyes are a layer drawn over the model full-bright,
/// <c>moth_eyes.png</c>, transparent but for the eyes. It doesn't spawn naturally: its category is <c>Misc</c>.
/// </remarks>
public sealed class MothMob : IEntityBehavior
{
    public const string Identifier = "sample:moth";

    /// <summary>
    /// The registered entity type, to spawn with <c>ILevel.SpawnEntity</c>.
    /// </summary>
    public static EntityDefinition Type { get; private set; } = null!;

    /// <summary>
    /// <c>sample:moth_rides</c>: how many times players got on a moth. It's saved with the moth, and kept as saved
    /// while the plugin is away.
    /// </summary>
    public static AttachmentType<int> Rides { get; private set; } = null!;

    /// <summary>
    /// Registers the entity type and its attachment. Both sides register the type the same, so that the client
    /// seats its rider where the server does.
    /// </summary>
    public static void Register(IPluginRegistry registry)
    {
        Type = registry.RegisterEntity(Identifier, new EntityProperties
        {
            Width = 0.9f,
            Height = 0.7f,
            MaxHealth = 10,
            SpawnEgg = true,
            Movement = EntityMovementMode.Flying,

            // One seat on its back, a little behind its middle (+Z is ahead).
            PassengerAttachments = [new VectorF(0, 0.65f, -0.1f)],
            Behavior = new MothMob()
        });

        // Only the server counts rides, so the attachment has no network codec: clients aren't sent it.
        Rides = registry.RegisterAttachment("sample:moth_rides", new ComponentCodec<int>(
            rides => new NbtTag<int>("", rides),
            tag => ((NbtTag<int>)tag).Value));
    }

    /// <summary>
    /// Registers how the client draws it: its model and texture, and its eyes over them, full-bright like vanilla's
    /// spider's and enderman's.
    /// </summary>
    public static void RegisterClient(IClientRegistry registry)
    {
        registry.RegisterEntityRenderer(Identifier, new EntityRendererProperties
        {
            Model = "sample:moth",
            Texture = "sample:entity/moth",
            ShadowRadius = 0.4f,
            Layers =
            [
                new EntityRenderLayer
                {
                    Texture = "sample:entity/moth_eyes",
                    RenderType = EntityRenderType.Emissive
                }
            ]
        });
    }

    /// <summary>
    /// A player who uses it without sneaking rides it, while its seat is free, and it counts the ride. A behaviour
    /// with nothing to count returns true from <c>CanMount</c> instead, and the engine seats the player.
    /// </summary>
    public ValueTask<bool> InteractAsync(IEntity entity, IPlayer player, InteractionHand hand)
    {
        if (player.Sneaking || !player.StartRiding(entity))
            return ValueTask.FromResult(false);

        // A get returns a detached value: set it again to change it.
        entity.SetAttachment(Rides, entity.GetAttachment(Rides) + 1);

        return ValueTask.FromResult(true);
    }
}
