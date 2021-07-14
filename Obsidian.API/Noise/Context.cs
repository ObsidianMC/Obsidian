
namespace Obsidian.API.Noise
{
    public class Context
    {

        protected long seedMixup;
        private long seed;
        private long random;

        public Context(int var1, long var2, long var4)
        {
            this.seedMixup = var4;
            this.seedMixup *= this.seedMixup * 6364136223846793005L + 1442695040888963407L;
            this.seedMixup += var4;
            this.seedMixup *= this.seedMixup * 6364136223846793005L + 1442695040888963407L;
            this.seedMixup += var4;
            this.seedMixup *= this.seedMixup * 6364136223846793005L + 1442695040888963407L;
            this.seedMixup += var4;
        }
    }
}
