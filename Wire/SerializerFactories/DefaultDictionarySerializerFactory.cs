﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Wire.ValueSerializers;

namespace Wire.SerializerFactories
{
    public class DefaultDictionarySerializerFactory : ValueSerializerFactory
    {
        public override bool CanSerialize(Serializer serializer, Type type) => IsInterface(type);

        private static bool IsInterface(Type type)
        {
            return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof (Dictionary<,>);
        }

        public override bool CanDeserialize(Serializer serializer, Type type) => IsInterface(type);

        public override ValueSerializer BuildSerializer(Serializer serializer, Type type,
            ConcurrentDictionary<Type, ValueSerializer> typeMapping)
        {
            var ser = new ObjectSerializer(type);
            typeMapping.TryAdd(type, ser);
            var elementSerializer = serializer.GetSerializerByType(typeof (DictionaryEntry));
            var preserveObjectReferences = serializer.Options.PreserveObjectReferences;
            ObjectReader reader = (stream, session) =>
            {
                var count = stream.ReadInt32(session);
                var instance = (IDictionary) Activator.CreateInstance(type, count);
                if (preserveObjectReferences)
                {
                    session.TrackDeserializedObject(instance);
                }

                for (var i = 0; i < count; i++)
                {
                    var entry = (DictionaryEntry) stream.ReadObject(session);
                    instance.Add(entry.Key, entry.Value);
                }
                return instance;
            };

            ObjectWriter writer = (stream, obj, session) =>
            {

                if (preserveObjectReferences)
                {
                    session.TrackSerializedObject(obj);
                }
                var dict = obj as IDictionary;
                // ReSharper disable once PossibleNullReferenceException
                stream.WriteInt32(dict.Count);
                foreach (DictionaryEntry item in dict)
                {
                    stream.WriteObject(item, typeof (DictionaryEntry), elementSerializer,
                        serializer.Options.PreserveObjectReferences, session);
                    // elementSerializer.WriteValue(stream,item,session);
                }
            };
            ser.Initialize(reader, writer);
            
            return ser;
        }
    }
}