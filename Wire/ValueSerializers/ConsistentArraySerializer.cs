using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Wire.ValueSerializers
{
    public class ConsistentArraySerializer : ValueSerializer
    {
        public const byte Manifest = 252;
        public static readonly ConsistentArraySerializer Instance = new ConsistentArraySerializer();

        public override object ReadValue(Stream stream, DeserializerSession session)
        {
            var elementSerializer = session.Serializer.GetDeserializerByManifest(stream, session);
            //read the element type
            var elementType = elementSerializer.GetElementType();
            //get the element type serializer
            var length = stream.ReadInt32(session);
            var array = Array.CreateInstance(elementType, length); //create the array
            if (session.Serializer.Options.PreserveObjectReferences)
            {
                session.TrackDeserializedObject(array);
            }

            for (var i = 0; i < length; i++)
            {
                var value = elementSerializer.ReadValue(stream, session); //read the element value
                array.SetValue(value, i); //set the element value
            }


            return array;
        }

        public override Type GetElementType()
        {
            throw new NotSupportedException();
        }

        public override void WriteManifest(Stream stream, Type type, SerializerSession session)
        {
            stream.WriteByte(Manifest);
        }

        public override void WriteValue(Stream stream, object value, SerializerSession session)
        {
            if (session.Serializer.Options.PreserveObjectReferences)
            {
                session.TrackSerializedObject(value);
            }
            var elementType = value.GetType().GetElementType();
            var elementSerializer = session.Serializer.GetSerializerByType(elementType);
            elementSerializer.WriteManifest(stream, elementType, session); //write array element type
            // ReSharper disable once PossibleNullReferenceException
            WriteValues((dynamic)value, stream,elementSerializer,session);

        }

        private static void WriteValues<T>(T[] array, Stream stream, ValueSerializer elementSerializer, SerializerSession session)
        {
            
            stream.WriteInt32(array.Length);
            if (Utils.IsFixedSizeType(typeof(T)))
            {
                var size = Utils.GetTypeSize(typeof(T));
                var result = new byte[array.Length * size];
                Buffer.BlockCopy(array, 0, result, 0, result.Length);
                stream.Write(result);
            }
            else
            {
                foreach (var value in array)
                {
                    elementSerializer.WriteValue(stream, value, session);
                }
            }    
        }
    }
}