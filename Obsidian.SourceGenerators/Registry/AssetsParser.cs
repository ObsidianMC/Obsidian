using Microsoft.CodeAnalysis;

namespace Obsidian.SourceGenerators.Registry
{
    internal abstract class AssetsParser
    {
        /// <summary>
        /// Name of the file to be used as an asset.
        /// </summary>
        public abstract string SourceFile { get; }
        /// <summary>
        /// Name of the result file.
        /// </summary>
        public abstract string Name { get; }

        public abstract void ParseAsset(GeneratorExecutionContext context, string asset);
    }
}
