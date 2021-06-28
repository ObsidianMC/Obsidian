using Microsoft.CodeAnalysis;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed partial class BlocksParser : AssetsParser
    {
        public override string SourceFile => "blocks.json";
        public override string Name => "Blocks";

        public override void ParseAsset(GeneratorExecutionContext context, string asset)
        {
            BlockHandler blockHandler = context.Compilation.AssemblyName switch
            {
                "Obsidian" => new RegistryBuilder(),
                "Obsidian.API" => new BlockBuilder(),
                _ => null
            };

            if (blockHandler is null)
                return;

            using var document = JsonDocument.Parse(asset);

            foreach (JsonProperty block in document.RootElement.EnumerateObject())
            {
                blockHandler.HandleBlock(context, block);
            }

            blockHandler.Complete(context);
        }

        private abstract class BlockHandler
        {
            public abstract void HandleBlock(GeneratorExecutionContext context, JsonProperty block);
            public abstract void Complete(GeneratorExecutionContext context);
        }
    }
}
