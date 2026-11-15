// ReSharper disable InconsistentNaming
using System.ComponentModel;

namespace Obsidian.API;

/// <summary>Minecraft protocol version numbers, one per supported game version.</summary>
public enum ProtocolVersion
{
    /// <summary>Minecraft 1.13.</summary>
    [Description("1.13")]
    v1_13 = 393,

    /// <summary>Minecraft 1.13.2.</summary>
    [Description("1.13.2")]
    v1_13_2 = 404,

    /// <summary>Minecraft 1.14.</summary>
    [Description("1.14")]
    v1_14 = 477,

    /// <summary>Minecraft 1.14.1.</summary>
    [Description("1.14.1")]
    v1_14_1 = 480,

    /// <summary>Minecraft 1.15.2.</summary>
    [Description("1.15.2")]
    v1_15_2 = 578,

    /// <summary>Minecraft 1.16.3.</summary>
    [Description("1.16.3")]
    v1_16_3 = 753,

    //1.16.4 & 1.16.5 have the same PVN
    /// <summary>Minecraft 1.16.4 and 1.16.5.</summary>
    [Description("1.16.5")]
    v1_16_5 = 754,

    /// <summary>Minecraft 1.17.1.</summary>
    [Description("1.17.1")]
    v1_17_1 = 756,

    /// <summary>Minecraft 1.18.1.</summary>
    [Description("1.18.1")]
    v1_18_1 = 757,

    /// <summary>Minecraft 1.18.2.</summary>
    [Description("1.18.2")]
    v1_18_2 = 758,

    /// <summary>Minecraft 1.19.</summary>
    [Description("1.19")]
    v1_19 = 759,

    /// <summary>Minecraft 1.19.2.</summary>
    [Description("1.19.2")]
    v1_19_2 = 760,

    /// <summary>Minecraft 1.19.3.</summary>
    [Description("1.19.3")]
    v1_19_3 = 761,

    /// <summary>Minecraft 1.19.4.</summary>
    [Description("1.19.4")]
    v1_19_4 = 762,

    /// <summary>Minecraft 1.20.</summary>
    [Description("1.20")]
    v1_20 = 763,

    /// <summary>Minecraft 1.20.2.</summary>
    [Description("1.20.2")]
    v1_20_2 = 764,

    //1.20.3 same pvn
    /// <summary>Minecraft 1.20.3 and 1.20.4.</summary>
    [Description("1.20.4")]
    v1_20_4 = 765,

    //1.20.5 same pvn
    /// <summary>Minecraft 1.20.5 and 1.20.6.</summary>
    [Description("1.20.6")]
    v1_20_6 = 766,

    //1.21 same pvn
    /// <summary>Minecraft 1.21 and 1.21.1.</summary>
    [Description("1.21.1")]
    v1_21_1 = 767,

    /// <summary>Minecraft 1.21.3.</summary>
    [Description("1.21.3")]
    v1_21_3 = 768,

    /// <summary>Minecraft 1.21.4.</summary>
    [Description("1.21.4")]
    v1_21_4 = 769,

    /// <summary>Minecraft 1.21.5.</summary>
    [Description("1.21.5")]
    v1_21_5 = 770,

    /// <summary>Minecraft 1.21.6.</summary>
    [Description("1.21.6")]
    v1_21_6 = 771,

    //Both 1.21.7 and 1.21.8 have the same PVN
    /// <summary>Minecraft 1.21.7 and 1.21.8.</summary>
    [Description("1.21.8")]
    v1_21_8 = 772,

    /// <summary>Minecraft 1.21.10.</summary>
    [Description("1.21.10")]
    v1_21_10 = 773,

    /// <summary>Minecraft 1.21.11.</summary>
    [Description("1.21.11")]
    v1_21_11 = 774,

    /// <summary>Minecraft 26.3.</summary>
    [Description("26.3")]
    v26_3 = 777,
}
