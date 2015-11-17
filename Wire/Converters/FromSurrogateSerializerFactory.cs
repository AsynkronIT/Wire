﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using Wire.ValueSerializers;

namespace Wire.Converters
{
    public class FromSurrogateSerializerFactory : ValueSerializerFactory
    {
        public override bool CanSerialize(Serializer serializer, Type type)
        {
            return false;
        }

        public override bool CanDeserialize(Serializer serializer, Type type)
        {
            var surrogate = serializer.Options.Surrogates.FirstOrDefault(s => s.To.IsAssignableFrom(type));
            return surrogate != null;
        }

        public override ValueSerializer BuildSerializer(Serializer serializer, Type type,
            ConcurrentDictionary<Type, ValueSerializer> typeMapping)
        {
            var surrogate = serializer.Options.Surrogates.FirstOrDefault(s => s.To.IsAssignableFrom(type));
            ObjectSerializer objectSerializer = new ObjectSerializer(type);
            var fromSurrogateSerializer = new FromSurrogateSerializer(surrogate.FromSurrogate, objectSerializer);
            typeMapping.TryAdd(type, fromSurrogateSerializer);


            CodeGenerator.BuildSerializer(serializer, type, objectSerializer);
            return fromSurrogateSerializer;
        }
    }
}