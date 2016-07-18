using System;
using System.IO;

namespace Wire.ValueSerializers
{
    public class Int16Serializer : ValueSerializer
    {
        public const byte Manifest = 3;
        public static readonly Int16Serializer Instance = new Int16Serializer();

        public override void WriteManifest(Stream stream, Type type, SerializerSession session)
        {
            stream.WriteByte(Manifest);
        }

        public override void WriteValue(Stream stream, object value, SerializerSession session)
        {
            var bytes = BitConverter.GetBytes((short) value);
            stream.Write(bytes);
        }

        public override object ReadValue(Stream stream, DeserializerSession session)
        {
            const int size = sizeof (short);
            var buffer = session.GetBuffer(size);
            stream.Read(buffer, 0, size);
            return BitConverter.ToInt16(buffer, 0);
        }

        public override Type GetElementType()
        {
            return typeof (short);
        }
    }
}