using System;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Select : CombineModule<float, float>
    {
        public Module<float> Control { get; init; }
        public float LowerBound { get; init; } = -1f;
        public float UpperBound { get; init; } = 1f;
        public float EdgeFalloff { get; init; }

        private readonly bool forceInlineSource0;
        private readonly bool forceInlineSource1;

        public Select() : this(false, false)
        {
        }

        public Select(bool forceInlineSource0 = false, bool forceInlineSource1 = false)
        {
            this.forceInlineSource0 = forceInlineSource0;
            this.forceInlineSource1 = forceInlineSource1;
        }

        protected override Expression GetCombineExpression(Expression input1, Expression input2, ParameterExpression input)
        {
            ModulesHelper.ThrowIfNull(Control);

            if (UpperBound < LowerBound)
                throw new ArgumentOutOfRangeException($"{nameof(UpperBound)} must be greater than {nameof(LowerBound)}");

            float boundsMiddle = (UpperBound - LowerBound) / 2f;
            float edgeFalloff = (EdgeFalloff > boundsMiddle) ? boundsMiddle : EdgeFalloff;

            Expression control = Control.GetExpression(input);

            ParameterExpression controlValue = Variable(typeof(float), nameof(controlValue));

            if (edgeFalloff <= 0f)
            {
                ConstantExpression lowerBound = Constant(LowerBound);
                ConstantExpression upperBound = Constant(UpperBound);

                return Block(
                    new[] { controlValue },
                    // controlValue = Control.GetValue(input);
                    Assign(controlValue, control),
                    // return (controlValue < LowerBound || controlValue > UpperBound) ? input1.GetValue(input) : input2.GetValue(input);
                    Condition(OrElse(LessThan(controlValue, lowerBound), GreaterThan(controlValue, upperBound)), input1, input2)
                );
            }

            MethodInfo lerp = typeof(MathHelper).GetMethod(nameof(MathHelper.Lerp), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

            ConstantExpression lowerPlusEdge = Constant(LowerBound + edgeFalloff);
            ConstantExpression lowerMinusEdge = Constant(LowerBound - edgeFalloff);
            ConstantExpression upperPlusEdge = Constant(UpperBound + edgeFalloff);
            ConstantExpression upperMinusEdge = Constant(UpperBound - edgeFalloff);

            // Extract potentially large expression blocks to separate function calls to reduce IL size
            Expression call1 = forceInlineSource0 ? input1 : input1.Compress(input, nameof(call1));
            Expression call2 = forceInlineSource1 ? input2 : input2.Compress(input, nameof(call2));

            BinaryExpression assignControlValue = Assign(controlValue, control);
            LabelTarget returnTarget = Label(typeof(float));

            float r = UpperBound - LowerBound;
            ConstantExpression x = Constant(-3f * LowerBound + -1.5f * r);
            ConstantExpression y = Constant(3f * LowerBound * (LowerBound + r));
            ConstantExpression z = Constant(-1.5f * LowerBound * LowerBound * r - LowerBound * LowerBound * LowerBound);
            ConstantExpression w = Constant(-0.5f * r * r * r);
            Expression case4 = IfThenElse(
                LessThan(controlValue, upperPlusEdge),
                Return(returnTarget, Call(lerp, call1, call2, Divide(Add(Multiply(controlValue, Add(Multiply(controlValue, Add(controlValue, x)), y)), z), w))),
                call1
            );

            Expression case3 = IfThenElse(
                LessThan(controlValue, upperMinusEdge),
                Return(returnTarget, call2),
                case4
            );

            Expression case2 = IfThenElse(
                LessThan(controlValue, lowerPlusEdge),
                Return(returnTarget, Call(lerp, call2, call1, Divide(Add(Multiply(controlValue, Add(Multiply(controlValue, Add(controlValue, x)), y)), z), w))),
                case3
            );

            Expression case1 = IfThenElse(
                LessThan(controlValue, lowerMinusEdge),
                Return(returnTarget, call1),
                case2
            );

            return Block(
                new[] { controlValue },
                assignControlValue,
                case1,
                Label(returnTarget, Constant(0f))
            );
        }
    }
}
