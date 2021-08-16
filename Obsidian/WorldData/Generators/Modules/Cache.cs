using System;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Cache<TIn, TOut> : StatefulModule<TIn, TOut> where TIn : IEquatable<TIn>
    {
        public Module<TOut> Source { get; init; }

        private CacheEntry<TIn, TOut>? cacheEntry;
        private Func<TIn, TOut> func;

        protected override void InitializeState()
        {
            ModulesHelper.ThrowIfNull(Source);

            func ??= Source.Compile<TIn>();
        }

        protected override TOut GetValue(TIn input)
        {
            if (cacheEntry.HasValue && cacheEntry.Value.Cache.Equals(input))
            {
                return cacheEntry.Value.Result;
            }

            TOut result = func(input);

            cacheEntry = new CacheEntry<TIn, TOut>()
            {
                Cache = input,
                Result = result
            };

            return result;
        }

        private readonly struct CacheEntry<TCache, TResult>
        {
            public TCache Cache { get; init; }
            public TResult Result { get; init; }
        }
    }
}
