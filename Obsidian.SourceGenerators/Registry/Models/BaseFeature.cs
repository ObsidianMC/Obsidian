using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class BaseFeature
{
    public string Name { get; set; } = default!;

    public List<JsonProperty> Properties { get; set; } = [];
}

internal sealed class BaseFeatureDictionary
{
    private readonly Dictionary<string, TypeInformation> heightProviders = [];
    private readonly Dictionary<string, TypeInformation> intProviders = [];
    private readonly Dictionary<string, TypeInformation> configuredFeatures = [];
    public Dictionary<string, TypeInformation> FeatureBaseClasses { get; } = [];

    public Dictionary<string, TypeInformation> FeatureTypes { get; } = [];

    public void AddHeightProvider(string key, TypeInformation value) => this.heightProviders.Add(key, value);
    public void AddIntProvider(string key, TypeInformation value) => this.intProviders.Add(key, value);
    public void AddConfiguredFeature(string key, TypeInformation value) => this.configuredFeatures.Add(key, value);
    public void AddFeatureBaseClass(string key, TypeInformation value) => this.FeatureBaseClasses.Add(key, value);
    public void AddFeatureType(string key, TypeInformation value) => this.FeatureTypes.Add(key, value);

    public bool TryGetValue(string key, out TypeInformation value)
    {
        if (this.heightProviders.TryGetValue(key, out value))
            return true;

        if (this.intProviders.TryGetValue(key, out value))
            return true;

        if (this.configuredFeatures.TryGetValue(key, out value))
            return true;

        return false;
    }

    public TypeInformation? GetValue(string key)
    {
        if (this.heightProviders.TryGetValue(key, out var value))
            return value;
        if (this.intProviders.TryGetValue(key, out value))
            return value;
        if (this.configuredFeatures.TryGetValue(key, out value))
            return value;

        return null;
    }
}
