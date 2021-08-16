using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class ScalePoint : TransformModule<float, float>
    {
        public Vector3 Scale { get; init; } = Vector3.One;

        protected override Expression GetTransformExpression(ParameterExpression input)
        {
            MethodInfo vectorMultiply = typeof(Vector3).GetMethod("op_Multiply", new[] { typeof(Vector3), typeof(Vector3) });
            ConstructorInfo vectorCtor = typeof(Vector3).GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            ParameterExpression scaledPoint = Expression.Variable(typeof(Vector3));
            return Expression.Block(
                new[] { scaledPoint },
                Expression.Assign(scaledPoint, Expression.Call(vectorMultiply, input,
                Expression.New(vectorCtor, Expression.Constant(Scale.X), Expression.Constant(Scale.Y), Expression.Constant(Scale.Z)))),
                Source.GetExpression(scaledPoint)
            );
        }
    }
}
