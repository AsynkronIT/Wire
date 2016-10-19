﻿using System;
using System.IO;
using Wire.Extensions;

namespace Wire.ValueSerializers
{
    public class UInt64Serializer : SessionAwareByteArrayRequiringValueSerializer<ulong>
    {
        public const byte Manifest = 19;
        public const int Size = sizeof(ulong);
        public static readonly UInt64Serializer Instance = new UInt64Serializer();

        public UInt64Serializer() : base(Manifest, () => WriteValueImpl, () => ReadValueImpl)
        {
        }

        public static void WriteValueImpl(Stream stream, ulong ul, byte[] bytes)
        {
            NoAllocBitConverter.GetBytes(ul, bytes);
            stream.Write(bytes, 0, Size);
        }

        public static ulong ReadValueImpl(Stream stream, byte[] bytes)
        {
            stream.Read(bytes, 0, Size);
            return BitConverter.ToUInt64(bytes, 0);
        }

        public override int PreallocatedByteBufferSize => Size;
    }
}