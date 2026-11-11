using Obsidian.API;
using Obsidian.Entities;
using Xunit;

namespace Obsidian.Tests.Utilities;

internal static class ClientRotationCases
{
    internal static readonly (float Yaw, float Pitch, float ExpectedYaw, float ExpectedPitch)[] Finite =
    [
        (float.MaxValue, 0, 0, 0),
        (-float.MaxValue, 0, 0, 0),
        (0, float.MaxValue, 0, 0),
        (0, -float.MaxValue, 0, 0),
        (720, 135, 0, 90),
        (-720, -135, 0, -90),
        (540, 315, -180, -45),
        (180, 180, -180, -90),
        (12.345f, -23.456f, 12.345f, -23.456f)
    ];

    internal static void AssertRotation(Entity entity, float yaw, float pitch)
    {
        Assert.Equal((yaw, pitch), (entity.Yaw, entity.Pitch));
        if (yaw == 0 && pitch == 0)
        {
            Assert.Equal(0, ((Angle)entity.Yaw).Value);
            Assert.Equal(new VectorF(0, 0, 1), entity.GetLookDirection());
        }
    }
}
