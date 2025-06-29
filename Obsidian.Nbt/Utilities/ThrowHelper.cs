using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Obsidian.Nbt.Utilities;

internal class ThrowHelper
{
    internal static void ThrowInvalidOperationException_StringTooLong()
    {
        throw new InvalidOperationException("Received string is longer than allowed.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ThrowOutOfRangeException_IfNegative(int value, [CallerArgumentExpression("value")] string? paramName = null)
    {
        if (value < 0)
        {
            ThrowOutOfRangeException_Negative(value, paramName!);
        }
    }

    internal static void ThrowOutOfRangeException_Negative(int value, string paramName)
    {
        throw new ArgumentOutOfRangeException($"Value of {paramName} must be positive or zero.");
    }

    [DoesNotReturn]
    internal static void ThrowInvalidOperationException_NotEnoughData()
    {
        throw new InvalidOperationException("There isn't enough buffered data for this operation.");
    }

    internal static void ThrowInvalidOperationException_InvalidInstance()
    {
        throw new InvalidOperationException("Instance was not properly initialized.");
    }

    internal static void ThrowInvalidOperationException_IncorrectTagType()
    {
        throw new InvalidOperationException("Tag type doesn't match requested data type.");
    }

    internal static void ThrowException_InsufficientBufferSize()
    {
        throw new Exception("Acquired buffer did not have sufficient size.");
    }
}
