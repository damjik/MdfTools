using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MdfTools.Shared;
using MdfTools.Shared.Data.Base;
using MdfTools.Shared.Data.Spec;

namespace MdfTools.V4
{
    public class Mdf4Channel : IDecodable
    {
        private readonly Mdf4CNBlock _cnBlock;

        private ValueDecoderSpec _spec;

        public string Name => _cnBlock.Name;
        public string Comment => _cnBlock.Comment;

        public string Source => _cnBlock.Source?.SourceName;
        public Mdf4ChannelGroup ChannelGroup { get; }

        public Mdf4Channel Master => ChannelGroup.MasterChannel;

        protected Mdf4Channel(Mdf4Channel adapter)
        {
            ChannelGroup = adapter.ChannelGroup;
            _cnBlock = adapter._cnBlock;
        }

        internal Mdf4Channel(Mdf4ChannelGroup channelGroup, Mdf4CNBlock cnBlock)
        {
            ChannelGroup = channelGroup;
            _cnBlock = cnBlock;

            if (_cnBlock.Data.ChannelType == Mdf4CNBlock.ChannelType.Master)
                channelGroup.MasterChannel = this;

            if (cnBlock.Data.ChannelType == Mdf4CNBlock.ChannelType.VariableLength)
                Check.NotImplementedSuppressed(new NotImplementedException("VariableLength Channel"));
        }

        public ValueDecoderSpec DecoderSpec => _spec ??= CreateDataSpecLazy();

        public SampleBuffer CreateBuffer(long length, bool noConversion = false)
        {
            using var time = Mdf4File.Metrics.Allocations.Measure(length * 8);
            return ChannelGroup.File.SampleBufferFactory.Allocate(this, length, noConversion);
        }

        private void CreateValueConversionSpec(Mdf4CCBlock c, ref ValueConversionSpec val, ref DisplayConversionSpec disp)
        {
            if (c == null)
                return;

            // var p = c.Params;

            if (c.Data.RefCount > 0 && c.Params.Length != c.Data.RefCount)
            {
                //Hacky: we assume all parameters are present before the next linked CC Block.
                //Hacky: we only parse the last CC Block.
                var blk = c.Ref(c.Data.RefCount - 1) as Mdf4CCBlock;
                if (blk != null)
                {
                    CreateValueConversionSpec(blk, ref val, ref disp);
                }
            }

            lock(ChannelGroup.File.ConversionCache)
            {
                if (ChannelGroup.File.ConversionCache.TryGetValue(c, out var specs))
                {
                    val = specs.Item1;
                    disp = specs.Item2;
                    return;
                }
            }

            void MappingHelper(int numBlocks, ref ValueConversionSpec val, ref DisplayConversionSpec disp)
            {
                var blks = Enumerable.Range(0, numBlocks).Select(k => c.Ref(k)).ToArray();
                var singleConversion = blks.OfType<Mdf4CCBlock>().ToArray();

                if (singleConversion.Length > 1)
                    Check.NotImplemented();

                else if (singleConversion.Length == 1)
                    CreateValueConversionSpec(singleConversion[0], ref val, ref disp);

                var lookup = new DisplayConversionSpec.LookupTable();
                for (int i = 0; i < blks.Length; i++)
                {
                    if (blks[i] is Mdf4TXBlock str)
                    {
                        lookup[c.Params[i]] = str;
                    }
                }

                disp = lookup;
            }

            switch (c.Data.ConversionType)
            {
            case Mdf4CCBlock.ConversionType.Identity:
                val = ValueConversionSpec.Default;
                break;
            case Mdf4CCBlock.ConversionType.Linear:
                if (c.Params[0] == 0 && c.Params[1] == 1)
                    val = ValueConversionSpec.Default;
                else
                    val = new ValueConversionSpec.Linear(c.Params[0], c.Params[1]);
                break;
            case Mdf4CCBlock.ConversionType.Rational:
                if (c.Params[0] == 0 && c.Params[3] == 0 && c.Params[4] == 0 && c.Params[5] == 1)
                {
                    if (c.Params[2] == 0 && c.Params[1] == 1)
                        val = ValueConversionSpec.Default;
                    else
                        val = new ValueConversionSpec.Linear(c.Params[2], c.Params[1]);
                }
                else
                    val = new ValueConversionSpec.Rational3(c.Params[2], c.Params[1], c.Params[0], c.Params[5], c.Params[4], c.Params[3]);

                break;
            case Mdf4CCBlock.ConversionType.AlgebraicText:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CCBlock.ConversionType.ValToValInterp:
                if (c.Params.Length == 4 && c.Params[0] == c.Params[1] && c.Params[2] == c.Params[3])
                    val = ValueConversionSpec.Default;
                else
                    Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CCBlock.ConversionType.ValToValNoInterp:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CCBlock.ConversionType.ValRangeToValTab:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CCBlock.ConversionType.ValToTextScaleTab:
            {
                MappingHelper(c.Params.Length, ref val, ref disp);
                break;
            }
            case Mdf4CCBlock.ConversionType.ValRangeToTextScaleTab:
            {
                var noRange = true;
                for (var i = 0; i < c.Params.Length; i += 2)
                    noRange &= c.Params[i] == c.Params[i + 1];

                if (!noRange)
                {
                    Check.NotImplemented();
                }

                MappingHelper(c.Params.Length / 2 + 1, ref val, ref disp);
                break;
            }
            case Mdf4CCBlock.ConversionType.TextToVal:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CCBlock.ConversionType.TextToText:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CCBlock.ConversionType.BitfieldText:
                Check.NotImplemented(new NotImplementedException());
                break;
            default:
                throw new ArgumentOutOfRangeException();
            }

            lock(ChannelGroup.File.ConversionCache)
            {
                ChannelGroup.File.ConversionCache[c] = (val, disp);
            }
            
            ChannelGroup.File.ConversionCache[c] = (val, disp);
        }

        private ValueDecoderSpec CreateDataSpecLazy()
        {
            int recordIdOffset = ChannelGroup.RecordIdSize;

            // var spec = new RawDecoderSpec();
            ref var data = ref _cnBlock.Data;
            var stride = ChannelGroup.RecordLength;
            var bitOffset = data.BitOffset;
            var byteOffset = (int) data.ByteOffset + recordIdOffset;
            var bitLength = (int) data.BitLength;
            var byteOrder = ByteOrder.Undefined;
            var dataType = DataType.Unknown;
            switch (data.DataType)
            {
            case Mdf4CNBlock.ChannelDataType.UnsignedLittleEndian:
                byteOrder = ByteOrder.LittleEndian;
                dataType = DataType.Unsigned;
                break;
            case Mdf4CNBlock.ChannelDataType.UnsignedBigEndian:
                byteOrder = ByteOrder.BigEndian;
                dataType = DataType.Unsigned;
                break;
            case Mdf4CNBlock.ChannelDataType.SignedLittleEndian:
                byteOrder = ByteOrder.LittleEndian;
                dataType = DataType.Signed;
                break;
            case Mdf4CNBlock.ChannelDataType.SignedBigEndian:
                byteOrder = ByteOrder.BigEndian;
                dataType = DataType.Signed;
                break;
            case Mdf4CNBlock.ChannelDataType.FloatLittleEndian:
                byteOrder = ByteOrder.LittleEndian;
                dataType = DataType.Float;
                break;
            case Mdf4CNBlock.ChannelDataType.FloatBigEndian:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.AnsiString:
                dataType = DataType.AnsiString;
                break;
            case Mdf4CNBlock.ChannelDataType.Utf8String:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.Utf16LeString:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.Utf18BeString:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.ByteArray:
                dataType = DataType.ByteArray;
                break;
            case Mdf4CNBlock.ChannelDataType.MIMESample:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.MIMEStream:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.CANopenDate:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.CANopenTime:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.ComplexLe:
                Check.NotImplemented(new NotImplementedException());
                break;
            case Mdf4CNBlock.ChannelDataType.ComplexBe:
                Check.NotImplemented(new NotImplementedException());
                break;
            default:
                throw new ArgumentOutOfRangeException();
            }

            var packSpec = new RawDecoderSpec(stride, byteOffset, bitOffset, bitLength, byteOrder, dataType);

            ValueConversionSpec valConv = ValueConversionSpec.Default;
            DisplayConversionSpec dispConv = DisplayConversionSpec.Default;
            CreateValueConversionSpec(_cnBlock.Conversion, ref valConv, ref dispConv);

            return new ValueDecoderSpec(packSpec, valConv, dispConv);
        }

        public override string ToString()
        {
            return $"{Name}/{ChannelGroup.Name}/{Source}";
        }
    }
}
