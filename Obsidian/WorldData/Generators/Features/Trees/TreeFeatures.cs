using Obsidian.WorldData.Generators.Features.Trees;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

// 26.3's trees and large features. Their config is at the root, and tree parts read 26.3's tags and providers.
internal sealed partial class FeatureRegistry
{
    private Feature Tree(JsonElement json)
    {
        var tree = new TreeConfig(this, json);

        return tree.Place;
    }

    private Feature FallenTree(JsonElement json)
    {
        var fallenTree = new FallenTreeFeature(this, json);

        return fallenTree.Place;
    }

    private Feature Spike(JsonElement json)
    {
        var spike = new SpikeFeature(this, json);

        return spike.Place;
    }

    private Feature HugeMushroom(JsonElement json, bool red)
    {
        HugeMushroomFeature mushroom = red
            ? new HugeRedMushroomFeature(this, json)
            : new HugeBrownMushroomFeature(this, json);

        return mushroom.Place;
    }

    private Feature HugeFungus(JsonElement json)
    {
        var fungus = new HugeFungusFeature(this, json);

        return fungus.Place;
    }
}
