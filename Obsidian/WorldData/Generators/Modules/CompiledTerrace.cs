using System;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class CompiledTerrace : TransformModule<float, float>
    {
        public float[] ControlPoints { get; init; }
        public bool Inverted { get; init; }

        protected override Expression GetTransformExpression(ParameterExpression input)
        {
            ModulesHelper.ThrowIfNull(Source);

            if (ControlPoints is null or { Length: 0 })
                throw new ArgumentException($"{nameof(ControlPoints)} can't be empty or null");

            MethodInfo min = typeof(Math).GetMethod(nameof(Math.Min), new[] { typeof(int), typeof(int) });
            MethodInfo max = typeof(Math).GetMethod(nameof(Math.Max), new[] { typeof(int), typeof(int) });
            MethodInfo lerp = typeof(MathHelper).GetMethod(nameof(MathHelper.Lerp));

            ConstantExpression points = Constant(ControlPoints);
            ConstantExpression pointsEnd = Constant(ControlPoints.Length - 1);
            ConstantExpression inverted = Constant(Inverted);

            MemberExpression pointsLength = Property(points, nameof(ControlPoints.Length));

            ParameterExpression sourceValue = Variable(typeof(float), nameof(sourceValue));
            ParameterExpression index = Variable(typeof(int), nameof(index));
            ParameterExpression index0 = Variable(typeof(int), nameof(index0));
            ParameterExpression index1 = Variable(typeof(int), nameof(index1));
            ParameterExpression value0 = Variable(typeof(float), nameof(value0));
            ParameterExpression value1 = Variable(typeof(float), nameof(value1));
            ParameterExpression alpha = Variable(typeof(float), nameof(alpha));
            ParameterExpression temp = Variable(typeof(float), nameof(temp));

            LabelTarget loopStart = Label();
            LabelTarget loopEnd = Label();
            LabelTarget returnTarget = Label(typeof(float));

            return Block(
                new[] { sourceValue, index, index0, index1, value0, value1, alpha },
                Assign(sourceValue, Source.GetExpression(input)),
                Label(loopStart),
                IfThen(LessThan(sourceValue, ArrayAccess(points, index)), Goto(loopEnd)),
                AddAssign(index, Constant(1)),
                IfThen(LessThan(index, pointsLength), Goto(loopStart)),
                Label(loopEnd),
                Assign(index0, Call(max, Subtract(index, Constant(1)), Constant(0))),
                Assign(index1, Call(min, index, pointsEnd)),
                IfThen(Equal(index0, index1), Return(returnTarget, ArrayAccess(points, index1))),
                Assign(value0, ArrayAccess(points, index0)),
                Assign(value1, ArrayAccess(points, index1)),
                Assign(alpha, Divide(Subtract(sourceValue, value0), Subtract(value1, value0))),
                IfThen(inverted, Block(
                    new[] { temp },
                    Assign(alpha, Subtract(Constant(1f), alpha)),
                    Assign(temp, value0),
                    Assign(value0, value1),
                    Assign(value1, temp)
                    )),
                MultiplyAssign(alpha, alpha),
                Return(returnTarget, Call(lerp, value0, value1, alpha)),
                Label(returnTarget, Constant(0f))
            );
        }
    }
}
