using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Obsidian.SourceGenerators.Packets.Attributes;

internal abstract class AttributeBehaviorBase
{
    public abstract string Name { get; }
    public abstract AttributeFlags Flag { get; }

    protected readonly AttributeSyntax syntax;
    private readonly AttributeArgumentSyntax[] arguments;

    public AttributeBehaviorBase(AttributeSyntax attributeSyntax)
    {
        syntax = attributeSyntax;
        arguments = attributeSyntax?.ArgumentList?.Arguments.Select(arg => arg).ToArray() ?? Array.Empty<AttributeArgumentSyntax>();
    }

    public virtual bool Matches(AttributeOwner other)
    {
        return true;
    }

    public virtual bool ModifySerialization(MethodBuildingContext context)
    {
        return false;
    }

    public virtual bool ModifyDeserialization(MethodBuildingContext context)
    {
        return false;
    }

    public virtual bool ModifyCollectionPrefixSerialization(MethodBuildingContext context)
    {
        return false;
    }

    public virtual bool ModifyCollectionPrefixDeserialization(MethodBuildingContext context)
    {
        return false;
    }

    protected bool TryGetArgument<TSyntax>(out TSyntax syntax) where TSyntax : ExpressionSyntax
    {
        if (arguments.Length > 0)
        {
            ExpressionSyntax expression = arguments[0].Expression;

            if (expression is TypeOfExpressionSyntax typeOfExpression)
            {
                expression = typeOfExpression.Type;
            }

            if (expression is TSyntax tSyntax)
            {
                syntax = tSyntax;
                return true;
            }
        }

        syntax = null;
        return false;
    }

    protected bool TryEvaluateIntArgument(out int argument)
    {
        argument = default;

        if (!TryGetArgument(out LiteralExpressionSyntax expression))
            return false;

        if (expression.Kind() != SyntaxKind.NumericLiteralExpression)
            return false;

        return int.TryParse(expression.Token.Text, out argument);
    }

    protected bool TryEvaluateTypeArgument(out string argument)
    {
        argument = null;

        if (!TryGetArgument(out TypeSyntax expression))
            return false;

        argument = expression.GetText().ToString();
        return true;
    }

    protected bool TryEvaluateStringArgument(out string argument)
    {
        argument = null;

        if (!TryGetArgument(out ExpressionSyntax expression))
            return false;

        return TryEvaluateString(expression, out argument);
    }

    private static bool TryEvaluateString(ExpressionSyntax expression, out string result)
    {
        result = expression switch
        {
            // "text"
            LiteralExpressionSyntax literal when literal.Kind() == SyntaxKind.StringLiteralExpression
                => literal.Token.ValueText,

            // nameof(Identifier)
            InvocationExpressionSyntax invocation when invocation.Expression.GetText().ToString() == "nameof"
                => invocation.ArgumentList.Arguments[0].Expression.GetText().ToString(),

            // "A" + "B"
            BinaryExpressionSyntax binaryAdd when binaryAdd.Kind() == SyntaxKind.AddExpression
                => TryEvaluateString(binaryAdd.Left, out var left) && TryEvaluateString(binaryAdd.Right, out var right) ? left + right : null,

            // $"A {B} C"
            InterpolatedStringExpressionSyntax interpolation
                => TryEvaluateInterpolatedString(interpolation, out var interpolatedText) ? interpolatedText : null,

            _ => null
        };

        return result is not null;
    }

    private static bool TryEvaluateInterpolatedString(InterpolatedStringExpressionSyntax interpolation, out string result)
    {
        SyntaxList<InterpolatedStringContentSyntax> contents = interpolation.Contents;

        var builder = new StringBuilder();
        for (int i = 0; i < contents.Count; i++)
        {
            InterpolatedStringContentSyntax content = contents[i];
            if (content is InterpolationSyntax interpolationSyntax && TryEvaluateString(interpolationSyntax.Expression, out string text))
            {
                builder.Append(text);
            }
            else if (content is InterpolatedStringTextSyntax textSyntax)
            {
                builder.Append(textSyntax.TextToken.ValueText);
            }
            else
            {
                result = null;
                return false;
            }
        }

        result = builder.ToString();
        return true;
    }
}
