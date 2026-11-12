using System;
using System.Collections.Generic;
using Obsidian.API;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Angles are sent to clients as one byte, 256ths of a turn, packed from the float rotation entities keep. Since
/// .NET 11 a float cast straight to a byte saturates rather than wrapping, which silently sent every negative angle as
/// 0; this guards the wrap.
/// </summary>
public sealed class AngleEncoding
{
    // Degrees and the byte vanilla's Mth.packDegrees gives them (floored, then the low byte).
    private static readonly (float Degrees, byte Packed)[] cases =
    [
        (0f, 0),
        (90f, 64),
        (359f, 255),
        (360f, 0),
        (450f, 64),
        (-1f, 255),
        (-90f, 192),
        (-180f, 128),
        (-450f, 192),
        // Just below zero floors to -1: wrapping into [0, 360) first would round it up to 360 and give 0.
        (-1e-10f, 255),
    ];

    [Fact]
    public void DegreesWrapIntoAByteLikeVanilla()
    {
        var wrong = new List<string>();
        foreach (var (degrees, packed) in cases)
        {
            var angle = new Angle { Degrees = degrees };
            var converted = (Angle)degrees;

            if (angle.Value != packed || converted.Value != packed)
                wrong.Add($"{degrees}: expected {packed}, setter gave {angle.Value}, conversion gave {converted.Value}");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
