﻿using System;

namespace MdfTools.Shared.Data.Base
{
    public class BufferView<TDecodable> : IDisposable where TDecodable : IDecodable
    {
        private readonly SampleBuffer _original;

        public TDecodable Channel => (TDecodable) _original.Decodable;

        public BufferView(SampleBuffer original)
        {
            _original = original;
        }

        public T[] GetData<T>()
        {
            return (T[]) _original.Data;
        }

        public Span<T> GetSpan<T>()
        {
            return ((SampleBuffer<T>) _original).Span;
        }

        public override string ToString()
        {
            return $"{Channel}: {_original.Data.Count} samples";
        }

        public void Dispose()
        {
            _original.Dispose();
        }
    }
}
