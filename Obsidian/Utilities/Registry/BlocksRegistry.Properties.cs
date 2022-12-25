using System.Linq.Expressions;

namespace Obsidian.Utilities.Registry;
internal partial class BlocksRegistry
{
    //Maybe we should make this a temp cache?
    private static readonly ConcurrentDictionary<string, Type> blockTypeCache = new();

    private static readonly ConcurrentDictionary<string, IBlock> defaultBlockCache = new();
    private static readonly ConcurrentDictionary<int, IBlock> blockWithStateCache = new();

    private static readonly ConcurrentDictionary<string, string> resourceIdToName = new();

    private static readonly Type[] stateIdParameters = new[] { typeof(int) };
    private static readonly ParameterExpression[] stateIdParameterExpressions = stateIdParameters.GetParamExpressions();

    private static readonly Type blockType = typeof(IBlock);
}
