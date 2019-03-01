namespace NbsPlayerPlugin
{
    public static class Constants
    {
        public const string Prefix = "[NBSPP] ";

        public static readonly int[] InstrumentValues = new int[]
        {
            106, //dirt, harp
            101, //wood, bass / double bass
            100,
            109, //sand, snare
            107, //glass, click / hat
            105, //wool, guitar
            104, //clay, flute
            102, //gold block, bell
            103, //packed ice, chime
            110, //bone block, xylophone
            //108, //???, pling
        };

        public static readonly float[] PitchValues = new float[]
        {
            0.5f,
            0.529732f,
            0.561234f,
            0.594604f,
            0.629961f,
            0.667420f,
            0.707107f,
            0.749154f,
            0.793701f,
            0.840896f,
            0.890899f,
            0.943874f,
            1f,
            1.059463f,
            1.122462f,
            1.189207f,
            1.259921f,
            1.334840f,
            1.414214f,
            1.498307f,
            1.587401f,
            1.681793f,
            1.781797f,
            1.887749f,
            2f
        };
    }
}