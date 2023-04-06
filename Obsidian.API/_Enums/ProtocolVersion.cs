// ReSharper disable InconsistentNaming
using System.ComponentModel;

namespace Obsidian.API;

public enum ProtocolVersion
{
    [Description("1.13")]
    v1_13 = 393,
    [Description("1.13.2")]
    v1_13_2 = 404,

    [Description("1.14")]
    v1_14 = 477,
    [Description("1.14.1")]
    v1_14_1 = 480,

    [Description("1.15.2")]
    v1_15_2 = 578,

    [Description("1.16.3")]
    v1_16_3 = 753,

    //1.16.4 & 1.16.5 have the same PVN
    [Description("1.16.5")]
    v1_16_5 = 754,

    [Description("1.17.1")]
    v1_17_1 = 756,

    [Description("1.18.1")]
    v1_18_1 = 757,

    [Description("1.18.2")]
    v1_18_2 = 758,

    [Description("1.19")]
    v1_19 = 759,

    [Description("1.19.2")]
    v1_19_2 = 760,

    [Description("1.19.3")]
    v1_19_3 = 761,

    [Description("1.19.4")]
    v1_19_4 = 762
}
