// Taj's COI Mods | Crc32ForwardingWriteStream.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;

namespace TajsCOI.Performance.Features.StreamingSaveCompression
{
    internal sealed class Crc32ForwardingWriteStream : Stream
    {
        private readonly Stream m_inner;
        private uint m_crc = uint.MaxValue;

        internal Crc32ForwardingWriteStream(Stream inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        internal uint Checksum => m_crc ^ uint.MaxValue;
        internal long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => m_inner.CanWrite;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() => m_inner.Flush();

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_inner.Write(buffer, offset, count);
            m_crc = Crc32Calculator.AppendRaw(m_crc, buffer, offset, count);
            BytesWritten += count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal static class Crc32Calculator
    {
        private static readonly uint[] s_table = CreateTable();

        internal static uint Compute(Stream input, out long bytesRead)
        {
            var buffer = new byte[64 * 1024];
            uint crc = uint.MaxValue;
            bytesRead = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc = AppendRaw(crc, buffer, 0, read);
                bytesRead += read;
            }
            return crc ^ uint.MaxValue;
        }

        internal static uint AppendRaw(uint crc, byte[] buffer, int offset, int count)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }

            int end = offset + count;
            for (int index = offset; index < end; index++)
            {
                crc = s_table[(byte)(crc ^ buffer[index])] ^ (crc >> 8);
            }
            return crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                uint entry = value;
                for (int bit = 0; bit < 8; bit++)
                {
                    entry = (entry & 1) != 0 ? 0xedb88320u ^ (entry >> 1) : entry >> 1;
                }
                table[value] = entry;
            }
            return table;
        }
    }
}
