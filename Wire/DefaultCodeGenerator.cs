using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Wire.ValueSerializers;
using Wire.ExpressionDSL;
using static System.Linq.Expressions.Expression;
using static Wire.ExpressionDSL.ExpressionEx;
namespace Wire
{
    public interface ICodeGenerator
    {
        void BuildSerializer(Serializer serializer, ObjectSerializer objectSerializer);
    }

    public class DefaultCodeGenerator : ICodeGenerator
    {
        public void BuildSerializer(Serializer serializer, ObjectSerializer objectSerializer)
        {
            var type = objectSerializer.Type;
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (objectSerializer == null)
                throw new ArgumentNullException(nameof(objectSerializer));

            var fields = ReflectionEx.GetFieldInfosForType(type);

            var fieldReaders = new List<FieldReader>();

            foreach (var field in fields)
            {
                fieldReaders.Add(GetFieldReader(serializer, type, field));
            }
            var writer = GetFieldsWriter(fields, serializer);

            var reader = serializer.Options.VersionTolerance
                ? GetVersionTolerantReader(type, serializer.Options.PreserveObjectReferences, fieldReaders)
                : GetVersionIntolerantReader(type, serializer, fields);

            objectSerializer.Initialize(reader, writer);
        }

        private ObjectReader GetVersionIntolerantReader(
            Type type,
           Serializer serializer,
            IEnumerable<FieldInfo> fields)
        {
            var expressions = Expressions();

            var streamParam = Parameter<Stream>("stream");
            var sessionParam = Parameter<DeserializerSession>("session");
            var newExpression = GetNewExpression(type);
            var targetVar = Variable<object>("target");
            var assignNewObjectToTarget = Assign(targetVar, newExpression);


            expressions.Add(assignNewObjectToTarget);

            if (serializer.Options.PreserveObjectReferences)
            {
                var trackDeserializedObjectMethod = typeof(DeserializerSession).GetMethod(nameof(DeserializerSession.TrackDeserializedObject));
                var call = Call(sessionParam, trackDeserializedObjectMethod, targetVar);
                expressions.Add(call);
            }

            foreach (var field in fields)
            {
                var fr = GetFieldReader2(serializer, type, field,targetVar,streamParam,sessionParam);
                expressions.Add(fr);
            }

            expressions.Add(targetVar);

            var body = expressions.ToBlock(targetVar);

            var readAllFields = Lambda<ObjectReader>(body, streamParam, sessionParam)
                .Compile();

            return readAllFields;
        }

        private static Expression GetNewExpression(Type type)
        {
            var defaultCtor = type.GetConstructor(new Type[] {});
            var il = defaultCtor?.GetMethodBody()?.GetILAsByteArray();
            var sideEffectFreeCtor = il != null && il.Length <= 8; //this is the size of an empty ctor
            if (sideEffectFreeCtor)
            {
                //the ctor exists and the size is empty. lets use the New operator
                return New(defaultCtor);
            }
            var emptyObjectMethod = typeof(TypeEx).GetMethod(nameof(TypeEx.GetEmptyObject));
            var emptyObject = Call(null, emptyObjectMethod, type.ToConstant());

            return emptyObject;
        }

        private ObjectReader GetVersionTolerantReader(Type type,
            bool preserveObjectReferences,
            IReadOnlyList<FieldReader> fieldReaders)
        {

            ObjectReader reader = (stream, session) =>
            {
                //create instance without calling constructor
                var instance = type.GetEmptyObject();
                if (preserveObjectReferences)
                {
                    session.TrackDeserializedObject(instance);
                }

                var versionInfo = session.GetVersionInfo(type);


                //for (var i = 0; i < storedFieldCount; i++)
                //{
                //    var fieldName = stream.ReadLengthEncodedByteArray(session);
                //    if (!Utils.UnsafeCompare(fieldName, fieldNames[i]))
                //    {
                //        //TODO: field name mismatch
                //        //this should really be a compare less equal or greater
                //        //to know if the field is added or removed

                //        //1) if names are equal, read the value and assign the field

                //        //2) if the field is less than the expected field, then this field is an unknown new field
                //        //we need to read this object and just ignore its content.

                //        //3) if the field is greater than the expected, we need to check the next expected until
                //        //the current is less or equal, then goto 1)
                //    }
                //}

                //this should be moved up in the version tolerant loop
                foreach (var fieldReader in fieldReaders)
                {
                    fieldReader(stream, instance, session);
                }

                return instance;
            };
            return reader;
        }

        //this generates a FieldWriter that writes all fields by unrolling all fields and calling them individually
        //no loops involved
        private ObjectWriter GetFieldsWriter(FieldInfo[] fields, Serializer serializer)
        {
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));

            var streamParam = Parameter<Stream>("stream");
            var objectParam = Parameter<object>("target");
            var sessionParam = Parameter<SerializerSession>("session");

            var expressions = Expressions();

            if (serializer.Options.PreserveObjectReferences)
            {
                var method =
                    typeof(SerializerSession).GetMethod(nameof(SerializerSession.TrackSerializedObject));
                var call = Call(sessionParam, method, objectParam);
                expressions.Add(call);
            }

            foreach (var field in fields)
            {
                var fieldWriter = GetFieldInfoWriter(serializer, field, streamParam, objectParam, sessionParam);
                expressions.Add(fieldWriter);
            }

            var body = expressions.ToBlock();
            var writeallFields =
                Lambda<ObjectWriter>(body, streamParam, objectParam,
                    sessionParam)
                    .Compile();
            return writeallFields;
        }

        private FieldReader GetFieldReader(
            Serializer serializer,
            Type type,
            FieldInfo field)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (field == null)
                throw new ArgumentNullException(nameof(field));

            FieldInfoWriter setter; // = GetSetDelegate(field);
            if (field.IsInitOnly)
            {
                //TODO: field is readonly, can we set it via IL or only via reflection
                setter = field.SetValue;
            }
            else
            {
                var targetExp = Parameter<object>("target");
                var valueExp = Parameter<object>("value");

                // ReSharper disable once PossibleNullReferenceException
                var castTartgetExp = field.DeclaringType.GetTypeInfo().IsValueType
                    ? Unbox(targetExp, type)
                    : targetExp.ConvertTo(type);
                
                var fieldExp = field.ReadFrom(castTartgetExp);
                var castValueExp = valueExp.ConvertTo(field.FieldType);
                var assignExp = Assign(fieldExp, castValueExp);
                setter = Lambda<FieldInfoWriter>(assignExp, targetExp, valueExp).Compile();
            }

            var s = serializer.GetSerializerByType(field.FieldType);
            if (!serializer.Options.VersionTolerance && field.FieldType.IsWirePrimitive())
            {
                //Only optimize if property names are not included.
                //if they are included, we need to be able to skip past unknown property data
                //e.g. if sender have added a new property that the receiveing end does not yet know about
                //which we cannot do w/o a manifest
                FieldReader fieldReader = (stream, o, session) =>
                {
                    var value = s.ReadValue(stream, session);
                    setter(o, value);
                };
                return fieldReader;
            }
            else
            {
                FieldReader fieldReader = (stream, o, session) =>
                {
                    var value = stream.ReadObject(session);
                    setter(o, value);
                };
                return fieldReader;
            }
        }

        private Expression GetFieldReader2(
            Serializer serializer,
            Type type,
            FieldInfo field, Expression targetExp, ParameterExpression stream, ParameterExpression session)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (field == null)
                throw new ArgumentNullException(nameof(field));

            var s = serializer.GetSerializerByType(field.FieldType);

            Expression read;
            if (!serializer.Options.VersionTolerance && field.FieldType.IsWirePrimitive())
            {
                //Only optimize if property names are not included.
                //if they are included, we need to be able to skip past unknown property data
                //e.g. if sender have added a new property that the receiveing end does not yet know about
                //which we cannot do w/o a manifest
                var method = typeof(ValueSerializer).GetMethod(nameof(ValueSerializer.ReadValue));
                read = Call(s.ToConstant(), method, stream, session);

            }
            else
            {
                var method = typeof(StreamExtensions).GetMethod(nameof(StreamExtensions.ReadObject));
                read = Call(null, method, stream, session);
            }

            Expression setter; 
            if (field.IsInitOnly)
            {
                //TODO: field is readonly, can we set it via IL or only via reflection

                var castTartgetExp = field.DeclaringType.GetTypeInfo().IsValueType
                    ? Unbox(targetExp, type)
                    : targetExp.ConvertTo(type);

                var method = typeof(FieldInfo).GetMethod(nameof(FieldInfo.SetValue),
                    new[] {typeof(object), typeof(object)});
                setter = Call(field.ToConstant(), method, castTartgetExp, read);
            }
            else
            {

                // ReSharper disable once PossibleNullReferenceException
                var castTartgetExp = field.DeclaringType.GetTypeInfo().IsValueType
                    ? Unbox(targetExp, type)
                    : targetExp.ConvertTo(type);

                var fieldExp = field.ReadFrom(castTartgetExp);
                var castValueExp = read.ConvertTo(field.FieldType);
                var assignExp = Assign(fieldExp, castValueExp);
                setter = assignExp;
            }

            return setter;
        }

        private Expression GetFieldInfoWriter(Serializer serializer, FieldInfo field, Expression stream,
            Expression targetExpression, Expression sessionExpression)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (field == null)
                throw new ArgumentNullException(nameof(field));

            //get the serializer for the type of the field
            var valueSerializer = serializer.GetSerializerByType(field.FieldType);
            //runtime Get a delegate that reads the content of the given field

            // ReSharper disable once PossibleNullReferenceException
            var castParam = field.DeclaringType.GetTypeInfo().IsValueType
                // ReSharper disable once AssignNullToNotNullAttribute
                ? Unbox(targetExpression, field.DeclaringType)
                // ReSharper disable once AssignNullToNotNullAttribute
                : targetExpression.ConvertTo(field.DeclaringType);
            var readField = field.ReadFrom(castParam);
            var fieldValue = readField.ConvertTo<object>();

            //if the type is one of our special primitives, ignore manifest as the content will always only be of this type
            if (!serializer.Options.VersionTolerance && field.FieldType.IsWirePrimitive())
            {
                //primitive types does not need to write any manifest, if the field type is known
                //nor can they be null (StringSerializer has it's own null handling)
                var method = typeof(ValueSerializer).GetMethod(nameof(ValueSerializer.WriteValue));
                //write it to the value serializer
                var writeValueCall = Call(valueSerializer.ToConstant(),
                    method, stream, fieldValue,
                    sessionExpression);

                return writeValueCall;
            }
            else
            {
                var valueType = field.FieldType;
                if (field.FieldType.IsNullable())
                {
                    var nullableType = field.FieldType.GetNullableElement();
                    valueSerializer = serializer.GetSerializerByType(nullableType);
                    valueType = nullableType;
                }

                var method = typeof(StreamExtensions).GetMethod(nameof(StreamExtensions.WriteObject));

                var writeValueCall = Call(null, method, stream, fieldValue, valueType.ToConstant(),
                    valueSerializer.ToConstant(), serializer.Options.PreserveObjectReferences.ToConstant(),
                    sessionExpression);

                return writeValueCall;
            }
        }
    }
}
