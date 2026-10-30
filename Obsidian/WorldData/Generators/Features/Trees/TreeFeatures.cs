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
}
