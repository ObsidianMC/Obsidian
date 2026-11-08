namespace Obsidian.WorldData.Generators.Terrain;

internal sealed partial class MaterialRules
{
    /// <summary>
    /// An ordered rule sequence with conditional jumps resolved while compiling. Evaluating a block needs no
    /// recursive sequence calls; custom rules remain leaves evaluated through their original delegates.
    /// </summary>
    private sealed class RuleProgram(Instruction[] instructions)
    {
        // Inlining children no longer than this bounds a program by its direct children's count times this.
        private const int MaxInlined = 256;

        private readonly Instruction[] instructions = instructions;

        public static Func<MaterialContext, IBlock?> Sequence(Func<MaterialContext, IBlock?>[] rules)
        {
            var instructions = new List<Instruction>();

            foreach (var rule in rules)
            {
                Append(instructions, rule);
            }

            return new RuleProgram(instructions.ToArray()).Apply;
        }

        public static Func<MaterialContext, IBlock?> When(
            Func<MaterialContext, bool> condition,
            Func<MaterialContext, IBlock?> rule)
        {
            var instructions = new List<Instruction> { default };
            Append(instructions, rule);
            instructions[0] = new Instruction(condition, null, instructions.Count);

            return new RuleProgram(instructions.ToArray()).Apply;
        }

        private static void Append(List<Instruction> target, Func<MaterialContext, IBlock?> rule)
        {
            // A larger child stays a call. Named rules can be referenced from many places, and each reference would
            // copy the whole child, so data that nests such references would grow the program exponentially.
            if (rule.Target is not RuleProgram program || program.instructions.Length > MaxInlined)
            {
                target.Add(new Instruction(null, rule, 0));
                return;
            }

            var offset = target.Count;

            // A child's end becomes the next sibling's start, including a jump that skips the whole child.
            foreach (var instruction in program.instructions)
            {
                target.Add(instruction with { OnFalse = instruction.OnFalse + offset });
            }
        }

        private IBlock? Apply(MaterialContext context)
        {
            var index = 0;

            // Vanilla evaluates conditions in data order, skips a failed condition's entire child, and returns
            // the first non-null result. Never hoist a test across another test or a rule's random/noise draws.
            while (index < this.instructions.Length)
            {
                ref readonly var instruction = ref this.instructions[index];

                if (instruction.Condition is Func<MaterialContext, bool> condition)
                {
                    index = condition(context) ? index + 1 : instruction.OnFalse;
                    continue;
                }

                var result = instruction.Rule!(context);

                if (result is not null)
                {
                    return result;
                }

                index++;
            }

            return null;
        }
    }

    private readonly record struct Instruction(
        Func<MaterialContext, bool>? Condition,
        Func<MaterialContext, IBlock?>? Rule,
        int OnFalse);
}
