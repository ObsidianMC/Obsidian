using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Entities;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A plugin's projectiles and objects touch blocks like vanilla's entities do. Vanilla has no plugin entities to
/// capture, so this guards the plugin API: such an entity resting on a wooden pressure plate must press it.
/// </summary>
public sealed class PluginEntityContacts
{
    [Fact]
    public async Task PluginObjectOnAWoodenPlatePressesIt()
    {
        await using var level = TestWorldFactory.Create("plugin-contacts");
        var blocks = new RedstoneTestWorld();
        blocks.Load(0, 15, 62, 67, 0, 15);
        var plate = new Vector(8, 64, 8);
        await blocks.Place(plate + Vector.Down, BlocksRegistry.Stone);
        await blocks.Place(plate, BlocksRegistry.OakPressurePlate);

        var definition = new EntityDefinition(10000, "test:crate", EntityKind.Object)
        {
            Settings = EntitySettings.From(new EntityProperties(), EntityKind.Object),
        };
        var crate = new PluginEntity(definition) { Level = level, Position = new VectorD(8.5, 64, 8.5) };
        blocks.Entities.Add(crate);

        await new EntityContactPass(blocks).TouchAsync(crate);

        Assert.True(EntityContactPass.TouchesBlocks(crate));
        Assert.Equal("true", blocks.GetLoadedBlock(plate)!.GetProperty("powered"));
    }
}
