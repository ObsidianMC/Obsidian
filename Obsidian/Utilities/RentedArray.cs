using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Obsidian.Utilities
{
    /// <summary>
    /// An array rented from a <see cref="ArrayPool{T}"/> that will be returned upon dispose
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public readonly struct RentedArray<T> : IDisposable
    {
        /// <summary>
        /// Number of <see cref="T"/> elements rented
        /// </summary>
        public int Length { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }
        private readonly ArrayPool<T> pool;
        private readonly T[] array;

        /// <summary>
        /// A <see cref="System.Span{T}"/> representation of the array
        /// </summary>
        public Span<T> Span => array.AsSpan(0, Length);
        
        /// <summary>
        /// A <see cref="System.Memory{T}"/> representation of the array
        /// </summary>
        public Memory<T> Memory => array.AsMemory(0, Length);

#nullable enable
        /// <summary>
        /// #nullable
        /// </summary>
        /// <param name="length"></param>
        /// <param name="pool"></param>
        public RentedArray(int length, ArrayPool<T>? pool = null)
        {
            this.pool = pool ?? ArrayPool<T>.Shared;
            array = this.pool.Rent(length);
            Length = length;
        }
#nullable disable

        /// <summary>
        /// Returns the rented array to the pool
        /// </summary>
        public void Dispose() => pool.Return(array);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Span<T>(in RentedArray<T> rentedArray) => rentedArray.Span;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(in RentedArray<T> rentedArray) => rentedArray.Span;
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Memory<T>(in RentedArray<T> rentedArray) => rentedArray.Memory;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlyMemory<T>(in RentedArray<T> rentedArray) => rentedArray.Memory;
    }
}