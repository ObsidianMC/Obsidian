namespace NbsPlayerPlugin
{
    public class NbsFile
    {
        public float Delay => 20 / Speed;
        public string Description { get; internal set; }
        public NbsLayer[] Layers { get; internal set; }
        public short Length { get; internal set; }
        public string OriginalSongAuthor { get; internal set; }
        public string SongAuthor { get; internal set; }
        public string SongName { get; internal set; }
        public float Speed => Tempo / 100f;
        public short Tempo { get; internal set; }
    }
}