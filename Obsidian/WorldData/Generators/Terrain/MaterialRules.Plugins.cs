using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain;

internal sealed partial class MaterialRules
{
    // A rule of a type beyond vanilla's (see IMaterialTypes), which gives the plugin's code the block's context, or
    // null when the type isn't one of those.
    private Func<MaterialContext, IBlock?>? CompilePluginRule(string type, JsonElement json)
    {
        if (this.types?.ReadRule(type, json, this) is not ICustomSurfaceRule rule)
            return null;

        return context => rule.Apply(context.Surface);
    }

    // A condition of a type beyond vanilla's, like CompilePluginRule.
    private Func<MaterialContext, bool>? CompilePluginCondition(string type, JsonElement json)
    {
        if (this.types?.ReadCondition(type, json, this) is not ICustomSurfaceCondition condition)
            return null;

        return context => condition.Test(context.Surface);
    }

    /// <summary>
    /// A material context as plugins' rules and conditions read it (see <see cref="ISurfaceContext"/>).
    /// </summary>
    /// <remarks>
    /// The rules and conditions a plugin's object holds were compiled for 26.3 as it was read (see
    /// <see cref="CompiledSurfaceRule"/>), and evaluate with this context; one the plugin made itself evaluates as a
    /// plugin's type, or as vanilla's class translated (see <see cref="Translate(ISurfaceRule)"/>).
    /// </remarks>
    private sealed class PluginSurfaceContext(MaterialRules rules, MaterialContext context) : ISurfaceContext
    {
        // How deep rules may evaluate others through contexts. Rules nest a few levels; one evaluating itself, or a
        // rule that leads back to it, would recurse until the stack overflows, ending the process rather than
        // failing the dimension, so it's stopped well before.
        private const int MaxDepth = 128;

        private int depth;

        public int BlockX => context.X;

        public int BlockY => context.Y;

        public int BlockZ => context.Z;

        public string Biome => context.Biome;

        public int SurfaceDepth => context.SurfaceDepth;

        public int StoneDepthAbove => context.Above;

        public int StoneDepthBelow => context.Below;

        public int WaterHeight => context.Water;

        public bool Steep => context.Steep;

        public int MinSurfaceLevel => context.MinSurface;

        public IBlock? Apply(ISurfaceRule rule)
        {
            this.Enter(rule);

            try
            {
                return rule switch
                {
                    CompiledSurfaceRule compiled => compiled.Rule(context),
                    ICustomSurfaceRule custom => custom.Apply(this),
                    _ => rules.Translate(rule)(context)
                };
            }
            finally
            {
                this.depth--;
            }
        }

        public bool Test(ISurfaceCondition condition)
        {
            this.Enter(condition);

            try
            {
                return condition switch
                {
                    CompiledSurfaceCondition compiled => compiled.Condition(context),
                    ICustomSurfaceCondition custom => custom.Test(this),
                    _ => rules.Translate(condition)(context)
                };
            }
            finally
            {
                this.depth--;
            }
        }

        private void Enter(IRegistryResource value)
        {
            if (this.depth >= MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Rules evaluated {MaxDepth} others deep through their context, at {value.Type}: does a plugin's "
                    + "rule or condition evaluate itself?");
            }

            this.depth++;
        }
    }
}
