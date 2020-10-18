// -----------------------------------------------------------------------
//   <copyright file="ObjectSerializer.cs" company="Asynkron HB">
//       Copyright (C) 2015-2017 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using Wire.Buffers;
using Wire.Extensions;

namespace Wire.ValueSerializers
{
    public class ObjectSerializer : ValueSerializer
    {
        public const byte ManifestFull = 255;
        public const byte ManifestIndex = 254;

        private readonly byte[] _manifest;

        private volatile bool _isInitialized;
        private int _preallocatedBufferSize;
        private ObjectReader _reader;
        private ObjectWriter<TBufferWriter> _writer;

        public ObjectSerializer(Type type)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            //TODO: remove version info
            var typeName = type.GetShortAssemblyQualifiedName();
            // ReSharper disable once PossibleNullReferenceException
            // ReSharper disable once AssignNullToNotNullAttribute
            var typeNameBytes = typeName.ToUtf8Bytes();


            //precalculate the entire manifest for this serializer
            //this helps us to minimize calls to Stream.Write/WriteByte 
            _manifest =
                new[] {ManifestFull}
                    .Concat(BitConverter.GetBytes(typeNameBytes.Length))
                    .Concat(typeNameBytes)
                    .ToArray(); //serializer id 255 + assembly qualified name

            //initialize reader and writer with dummy handlers that wait until the serializer is fully initialized
            _writer = writer;

            _reader = (stream, session) =>
            {
                SpinWait.SpinUntil(() => _isInitialized);
                return ReadValue(stream, session);
            };
            
            void writer<TBufferWriter>(ref Writer<TBufferWriter> stream, object o, SerializerSession session) where TBufferWriter:IBufferWriter<byte>
            {
                SpinWait.SpinUntil(() => _isInitialized);
                WriteValue(stream, o, session);
            }
        }

        public Type Type { get; }

        public override int PreallocatedByteBufferSize => _preallocatedBufferSize;

        public override void WriteManifest<TBufferWriter>(Writer<TBufferWriter> writer, SerializerSession session)
        {
            if (session.ShouldWriteTypeManifest(Type, out var typeIdentifier))
            {
                session.TrackSerializedType(Type);
                writer.Write(_manifest);
            }
            else
            {
                writer.Write(ManifestIndex);
            }
        }

        public override void WriteValue<TBufferWriter>(Writer<TBufferWriter> writer, object value, SerializerSession session)
        {
            _writer(writer, value, session);
        }

        public override object ReadValue(Stream stream, DeserializerSession session)
        {
            return _reader(stream, session);
        }

        public override Type GetElementType()
        {
            return Type;
        }

        public void Initialize(ObjectReader reader, ObjectWriter writer, int preallocatedBufferSize = 0)
        {
            _preallocatedBufferSize = preallocatedBufferSize;
            _reader = reader;
            _writer = writer;
            _isInitialized = true;
        }
    }
}