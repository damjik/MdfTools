﻿// With managed allocations of very long channels
// the large object heap gets filled quite fast
// when loading multiple large files.

using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MdfTools.Shared.Data.Base;
using MdfTools.Shared.Data.Spec;

namespace MdfTools.Shared.Data
{
    public class NumericBufferFactory : SampleBufferFactory
    {
        public override SampleBuffer Allocate(IDecodable channel, long length, bool noConversion)
        {
            var decoder = channel.DecoderSpec;
            var raw = decoder.RawDecoderSpec;
            var conv = decoder.ValueConversionSpec.ConversionType;
            NumericBufferBase ret = null;

            if (raw.IsSameEndianess &&
                conv == ValueConversionType.Linear ||
                conv == ValueConversionType.Identity)
                ret = new LinearBuffer(channel, length);

            //TODO: add endianess swapped buffer when we have access to validation data.

            if (noConversion)
                ret.DisableConversion();

            if (ret == null) Check.PleaseSendMeYourFile();

            return ret;
        }

#if USE_NATIVE_ALLOCATIONS
        private unsafe class LinearBuffer : NumericBufferBaseNative
#else
        private class LinearBuffer : NumericBufferBaseManaged
#endif
        {
            private readonly ulong _mask;
            private readonly int _shift;
            private ValueConversionSpec.Linear _conv;

            public LinearBuffer(IDecodable decodable, long length) : base(decodable, length)
            {
                _conv = Val as ValueConversionSpec.Linear ?? ValueConversionSpec.LinearIdentity;
                _mask = Raw.Mask;
                _shift = Raw.Shift;
            }

            public override void DisableConversion()
            {
                _conv = ValueConversionSpec.LinearIdentity;
            }

            public override void Update(Span<byte> raw, ulong offset, ulong sampleStart, uint sampleCount)
            {
                var str = (int) (offset + (ulong) Raw.TotalByteOffset);

                unchecked
                {
                    switch (Raw.NativeType)
                    {
                    case NativeType.NotNative:
                        Check.ThrowUnexpectedExecutionPath();
                        break;
                    case NativeType.UInt8:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (byte) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.UInt16:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (ushort) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.UInt32:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (uint) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.UInt64:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.Int8:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (sbyte) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.Int16:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (short) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.Int32:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (int) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.Int64:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<ulong>(ref raw[str]);
                            Storage[i] = (long) ((value >> _shift) & _mask) * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.Float:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<float>(ref raw[str]);
                            Storage[i] = value * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    case NativeType.Double:
                        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
                        {
                            var value = Unsafe.ReadUnaligned<double>(ref raw[str]);
                            Storage[i] = value * _conv.Scale + _conv.Offset;
                            str += Stride;
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                    }
                }
            }

        }
    }

    public abstract class NumericBufferBaseManaged : NumericBufferBase
    {
        protected readonly double[] Storage;
        public sealed override IList Data => Storage;
        public override Span<double> Span => Storage.AsSpan();

        protected NumericBufferBaseManaged(IDecodable decodable, long length) : base(decodable)
        {
            Storage = new double[length];
        }
    }

    public abstract unsafe class NumericBufferBaseNative : NumericBufferBase
    {
        internal readonly IntPtr HeapArray;
        protected readonly long Length;

        protected readonly double* Storage;
        public sealed override IList Data => null; //TODO: hmm... maybe change the interface?
        public override Span<double> Span => new Span<double>(HeapArray.ToPointer(), (int) Length);

        protected NumericBufferBaseNative(IDecodable decodable, long length) : base(decodable)
        {
            Length = length;
            HeapArray = Marshal.AllocHGlobal((IntPtr) (length * Unsafe.SizeOf<double>()));
            Storage = (double*) HeapArray.ToPointer();
        }

        private void ReleaseUnmanagedResources()
        {
            Marshal.FreeHGlobal(HeapArray);
        }

        protected virtual void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing)
            {
            }
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~NumericBufferBaseNative()
        {
            Dispose(false);
        }
    }

    public abstract class NumericBufferBase : SampleBuffer<double>
    {
        protected readonly RawDecoderSpec Raw;

        protected readonly int Stride;
        protected readonly ValueConversionSpec Val;

        protected NumericBufferBase(IDecodable decodable) : base(decodable)
        {
            var decoder = decodable.DecoderSpec;
            Raw = decoder.RawDecoderSpec;
            Val = decoder.ValueConversionSpec;
            Stride = (int) Raw.Stride;
        }

        public virtual void DisableConversion()
        {
        }

        public override void Dispose()
        {
        }
    }
}
