/*
 * This file is part of the Buildings and Habitats object Model (BHoM)
 * Copyright (c) 2015 - 2026, the respective contributors. All rights reserved.
 *
 * Each contributor holds copyright over their respective contributions.
 * The project versioning (Git) records all such contribution source information.
 *
 *
 * The BHoM is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3.0 of the License, or
 * (at your option) any later version.
 *
 * The BHoM is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this code. If not, see <https://www.gnu.org/licenses/lgpl-3.0.html>.
 */

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    // Compiled writer: the same encoding as the reflection reference writer of the BHoM_Proto tests, byte for byte, but with
    // every decision about a C# type taken once, when its plan is built:
    // - A MessagePlan<T> per (schema, C# type, message) holds one typed field writer per property, with a compiled getter
    //   and a codec chosen for the declared property type, so writing an object never uses reflection or boxes values.
    // - Writing runs in two passes over the object: the size pass computes the length of every nested message and records it
    //   in WriteContext.Sizes, in the order the write pass meets them; the write pass then writes everything into a single
    //   buffer of exactly the right size, instead of encoding each nested message into its own temporary byte array.
    // - Values whose encoding is computed by the existing well-known-type writers (timestamps, Struct, ListValue, wrappers)
    //   are encoded once in the size pass and kept in WriteContext.Blobs for the write pass.
    // Invariant: Size and Write of a codec take the same decisions for the same value, reserve and consume the same Sizes
    // and Blobs entries in the same order, and Size returns 0 exactly when Write writes nothing.
    internal static class WritePlans
    {
        /***************************************************/
        /**** Encoding                                  ****/
        /***************************************************/

        // The message of an object, e.g. for ToProtobuf.
        public static byte[] Encode(IMessagePlan plan, object obj)
        {
            WriteContext context = new WriteContext();
            byte[] buffer = new byte[plan.SizeObject(obj, context)];
            context.Output = new CodedOutputStream(buffer);
            plan.WriteObject(obj, context);
            context.Output.Flush();
            context.Output.CheckNoSpaceLeft();
            return buffer;
        }

        /***************************************************/

        // The message of an object wrapped in a google.protobuf.Any, e.g. for ToProtobufAny.
        public static byte[] EncodeAny(IMessagePlan plan, object obj)
        {
            WriteContext context = new WriteContext();
            string url = ProtoTypes.TypeUrlPrefix + plan.Message.FullName;
            int inner = plan.SizeObject(obj, context);
            int size = AnyEnvelopeSize(url, inner);

            byte[] buffer = new byte[size];
            context.Output = new CodedOutputStream(buffer);
            WriteAnyEnvelope(url, inner, context);
            plan.WriteObject(obj, context);
            context.Output.Flush();
            context.Output.CheckNoSpaceLeft();
            return buffer;
        }

        /***************************************************/
        /**** Plans                                     ****/
        /***************************************************/

        // The plan of a C# type for a message, built once and kept for the process, like the schema it belongs to.
        public static IMessagePlan Get(ProtoSchema schema, Type type, MessageDescriptor message)
        {
            (ProtoSchema, Type, MessageDescriptor) key = (schema, type, message);
            if (m_Plans.TryGetValue(key, out IMessagePlan plan))
                return plan;

            lock (m_Lock)
            {
                if (m_Plans.TryGetValue(key, out plan) || m_Pending.TryGetValue(key, out plan))
                    return plan;

                // Plans of types that refer to each other, directly or not, get each other's plan before it is complete:
                // the plan is registered as pending first, and all pending plans are published once the outermost is built.
                plan = (IMessagePlan)Activator.CreateInstance(typeof(MessagePlan<>).MakeGenericType(type), schema, message);
                m_Pending[key] = plan;
                m_Depth++;
                try
                {
                    plan.Initialise();
                }
                finally
                {
                    m_Depth--;
                    if (m_Depth == 0)
                    {
                        foreach (KeyValuePair<(ProtoSchema, Type, MessageDescriptor), IMessagePlan> pending in m_Pending)
                            m_Plans[pending.Key] = pending.Value;
                        m_Pending.Clear();
                    }
                }
                return plan;
            }
        }

        /***************************************************/
        /**** Codecs                                    ****/
        /***************************************************/

        // The codec of a value of a declared C# type in a field: the same classification as the reference writer, in the
        // same order, taken once per field instead of once per value.
        public static ICodec CreateCodec(Type type, FieldDescriptor field, bool force, ProtoSchema schema)
        {
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
            {
                // proto3 'optional': an explicitly set value is always written, even when it is the default.
                ICodec inner = CreateCodec(nullable, field, true, schema);
                return New(typeof(NullableCodec<>).MakeGenericType(nullable), inner);
            }

            if (ProtoTypes.IsTwoDimensionalArray(type))
            {
                FieldDescriptor items = field.MessageType.FindFieldByNumber(1);
                Type rowType = type.GetElementType().MakeArrayType();
                return New(typeof(TwoDimensionalArrayCodec<>).MakeGenericType(type), field.FieldNumber, type.GetElementType(), CreateCodec(rowType, items, true, schema));
            }

            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
            {
                // Both map<,> entries and the key/value wrapper messages are messages {1: key, 2: value}.
                ICodec keyCodec = CreateCodec(keyType, field.MessageType.FindFieldByNumber(1), true, schema);
                ICodec valueCodec = CreateCodec(valueType, field.MessageType.FindFieldByNumber(2), true, schema);
                return New(typeof(MapCodec<,,>).MakeGenericType(type, keyType, valueType), field.FieldNumber, keyCodec, valueCodec);
            }

            // Includes FragmentSet: a collection of IFragment, each a google.protobuf.Any (or a union).
            if (ProtoTypes.IsCollection(type, out Type itemType))
            {
                if (ProtoTypes.IsCollection(itemType, out _))
                {
                    // Nested collection: each inner collection wrapped in a message { repeated items = 1; }.
                    ICodec itemsCodec = CreateCodec(itemType, field.MessageType.FindFieldByNumber(1), true, schema);
                    return New(typeof(NestedRepeatedCodec<,>).MakeGenericType(type, itemType), field.FieldNumber, itemsCodec);
                }

                return New(typeof(RepeatedCodec<,>).MakeGenericType(type, itemType), CreateCodec(itemType, field, true, schema));
            }

            if (ProtoTypes.IsAnyValue(type))
                return New(typeof(AnyCodec<>).MakeGenericType(type), field.FieldNumber, schema);

            if (ProtoTypes.IsTuple(type))
            {
                Type[] itemTypes = type.GetGenericArguments();
                List<ICodec> itemCodecs = itemTypes.Select((t, i) => CreateCodec(t, field.MessageType.FindFieldByNumber(i + 1), true, schema)).ToList();
                return New(typeof(TupleCodec<>).MakeGenericType(type), field.FieldNumber, itemCodecs);
            }

            ICodec scalar = ScalarCodec(type, field.FieldNumber, force);
            if (scalar != null)
                return scalar;

            if (field.FieldType != FieldType.Message)
                return New(typeof(MismatchCodec<>).MakeGenericType(type), field);

            MessageDescriptor message = field.MessageType;
            if (ProtoSchema.IsAny(message))
                return New(typeof(AnyCodec<>).MakeGenericType(type), field.FieldNumber, schema);
            if (ProtoSchema.IsUnion(message))
                return New(typeof(UnionCodec<>).MakeGenericType(type), field.FieldNumber, message, schema);

            return New(typeof(MessageCodec<>).MakeGenericType(type), field.FieldNumber, Get(schema, type, message));
        }

        /***************************************************/

        // Values with a fixed encoding that does not depend on the schema. Null for messages.
        private static ICodec ScalarCodec(Type type, int number, bool force)
        {
            if (type == typeof(string)) return new StringCodec(number, force);
            if (type == typeof(Guid)) return new TextCodec<Guid>(number, g => g.ToString());
            if (type == typeof(char)) return new TextCodec<char>(number, c => c.ToString());
            if (type == typeof(double)) return new DoubleCodec(number, force);
            if (type == typeof(decimal)) return new ConvertCodec<decimal, double>(m => (double)m, new DoubleCodec(number, force));
            if (type == typeof(float)) return new FloatCodec(number, force);
            if (type == typeof(bool)) return new BoolCodec(number, force);
            if (type == typeof(int)) return new Int32Codec(number, force);
            if (type == typeof(short)) return new ConvertCodec<short, int>(v => v, new Int32Codec(number, force));
            if (type == typeof(byte)) return new ConvertCodec<byte, int>(v => v, new Int32Codec(number, force));
            if (type == typeof(sbyte)) return new ConvertCodec<sbyte, int>(v => v, new Int32Codec(number, force));
            if (type == typeof(long)) return new Int64Codec(number, force);
            if (type == typeof(uint)) return new UInt32Codec(number, force);
            if (type == typeof(ushort)) return new ConvertCodec<ushort, uint>(v => v, new UInt32Codec(number, force));
            if (type == typeof(ulong)) return new UInt64Codec(number, force);
            if (type == typeof(DateTime)) return new BlobCodec<DateTime>(number, EncodeTimestamp);
            if (type == typeof(DateTimeOffset)) return new BlobCodec<DateTimeOffset>(number, d => EncodeTimestamp(d.UtcDateTime));
            if (type == typeof(TimeSpan)) return new BlobCodec<TimeSpan>(number, EncodeDuration);
            if (typeof(Type).IsAssignableFrom(type))
            {
                Delegate fullName = Compile(type, typeof(string), p => Expression.Property(Expression.Convert(p, typeof(Type)), nameof(Type.FullName)));
                return New(typeof(TextCodec<>).MakeGenericType(type), number, fullName);
            }

            if (type.FullName == "System.Drawing.Color")
            {
                Delegate argb = Compile(type, typeof(uint), p => Expression.Convert(Expression.Call(p, type.GetMethod("ToArgb")), typeof(uint)));
                return New(typeof(ConvertCodec<,>).MakeGenericType(type, typeof(uint)), argb, new Fixed32Codec(number, true));
            }

            if (type.IsEnum)
            {
                if (ProtoTypes.IsOmType(type))
                {
                    Delegate toNumber = Compile(type, typeof(int), p => Expression.Convert(p, typeof(int)));
                    return New(typeof(ConvertCodec<,>).MakeGenericType(type, typeof(int)), toNumber, new Int32Codec(number, force));
                }

                // Enums of other libraries: written as their value name.
                Delegate toName = Compile(type, typeof(string), p => Expression.Call(Expression.Convert(p, typeof(object)), typeof(object).GetMethod(nameof(ToString))));
                return New(typeof(TextCodec<>).MakeGenericType(type), number, toName);
            }

            return null;
        }

        /***************************************************/
        /**** Any                                       ****/
        /***************************************************/

        // google.protobuf.Any { string type_url = 1; bytes value = 2; }
        public static int AnyEnvelopeSize(string url, int inner)
        {
            return m_TypeUrlTagSize + CodedOutputStream.ComputeStringSize(url) + m_ValueTagSize + CodedOutputStream.ComputeLengthSize(inner) + inner;
        }

        /***************************************************/

        // Writes the envelope of a google.protobuf.Any up to the value's content, which the caller writes next.
        public static void WriteAnyEnvelope(string url, int inner, WriteContext context)
        {
            context.Output.WriteTag(m_TypeUrlTag);
            context.Output.WriteString(url);
            context.Output.WriteTag(m_ValueTag);
            context.Output.WriteLength(inner);
        }

        /***************************************************/
        /**** Well-known types                          ****/
        /***************************************************/

        // A google.protobuf.Any holding a value without a message of its own: a dictionary as a google.protobuf.Struct,
        // a list as a google.protobuf.ListValue, a single value as a wrapper (DoubleValue, StringValue, ...).
        // Null, with a warning, for values that cannot be written as any of these.
        public static byte[] EncodeWellKnownAny(object value)
        {
            string typeName;
            byte[] content;
            if (value is IDictionary<string, object> dictionary)
            {
                typeName = "google.protobuf.Struct";
                content = EncodeStruct(dictionary);
            }
            else if (value is IEnumerable list && !(value is string))
            {
                // google.protobuf.ListValue { repeated Value values = 1; }
                List<byte[]> items = list.Cast<object>().Select(EncodeStructValue).ToList();
                if (items.Any(x => x == null))
                {
                    Base.Compute.RecordWarning($"A list of type {value.GetType().FullName} holds values that cannot be written as google.protobuf.Any and has been left out.");
                    return null;
                }
                typeName = "google.protobuf.ListValue";
                content = Encode(output => items.ForEach(item => WriteBytes(output, 1, item)));
            }
            else
            {
                if (value is Guid || value is Enum || value is char)
                    value = value.ToString();

                typeName = WrapperTypeName(value);
                if (typeName == null)
                {
                    Base.Compute.RecordWarning($"Values of type {value.GetType().FullName} cannot be written as google.protobuf.Any and have been left out.");
                    return null;
                }
                content = Encode(output => WriteWrapperValue(output, value));
            }

            return Encode(output =>
            {
                output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                output.WriteString(ProtoTypes.TypeUrlPrefix + typeName);
                WriteBytes(output, 2, content);
            });
        }

        /***************************************************/

        // google.protobuf.Timestamp { int64 seconds = 1; int32 nanos = 2; }
        public static byte[] EncodeTimestamp(DateTime dateTime)
        {
            long ticks = dateTime.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            return EncodeSecondsAndNanos(ticks);
        }

        /***************************************************/

        // google.protobuf.Duration { int64 seconds = 1; int32 nanos = 2; }
        public static byte[] EncodeDuration(TimeSpan timeSpan)
        {
            return EncodeSecondsAndNanos(timeSpan.Ticks);
        }

        /***************************************************/

        private static byte[] EncodeSecondsAndNanos(long ticks)
        {
            long seconds = ticks / TimeSpan.TicksPerSecond;
            int nanos = (int)(ticks % TimeSpan.TicksPerSecond) * 100;
            if (nanos < 0)
            {
                seconds -= 1;
                nanos += 1000000000;
            }

            return Encode(output =>
            {
                if (seconds != 0)
                {
                    output.WriteTag(1, WireFormat.WireType.Varint);
                    output.WriteInt64(seconds);
                }
                if (nanos != 0)
                {
                    output.WriteTag(2, WireFormat.WireType.Varint);
                    output.WriteInt32(nanos);
                }
            });
        }

        /***************************************************/

        private static string WrapperTypeName(object value)
        {
            switch (value)
            {
                case double _: return "google.protobuf.DoubleValue";
                case float _: return "google.protobuf.FloatValue";
                case int _: return "google.protobuf.Int32Value";
                case long _: return "google.protobuf.Int64Value";
                case uint _: return "google.protobuf.UInt32Value";
                case ulong _: return "google.protobuf.UInt64Value";
                case bool _: return "google.protobuf.BoolValue";
                case string _: return "google.protobuf.StringValue";
                default: return null;
            }
        }

        /***************************************************/

        // The value field (1) of a wrapper, always written, even when it is the default.
        private static void WriteWrapperValue(CodedOutputStream output, object value)
        {
            switch (value)
            {
                case double d:
                    output.WriteTag(1, WireFormat.WireType.Fixed64);
                    output.WriteDouble(d);
                    break;
                case float f:
                    output.WriteTag(1, WireFormat.WireType.Fixed32);
                    output.WriteFloat(f);
                    break;
                case int i:
                    output.WriteTag(1, WireFormat.WireType.Varint);
                    output.WriteInt32(i);
                    break;
                case long l:
                    output.WriteTag(1, WireFormat.WireType.Varint);
                    output.WriteInt64(l);
                    break;
                case uint u:
                    output.WriteTag(1, WireFormat.WireType.Varint);
                    output.WriteUInt32(u);
                    break;
                case ulong ul:
                    output.WriteTag(1, WireFormat.WireType.Varint);
                    output.WriteUInt64(ul);
                    break;
                case bool b:
                    output.WriteTag(1, WireFormat.WireType.Varint);
                    output.WriteBool(b);
                    break;
                case string text:
                    output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                    output.WriteString(text);
                    break;
            }
        }

        /***************************************************/

        // google.protobuf.Struct { map<string, Value> fields = 1; }
        private static byte[] EncodeStruct(IDictionary<string, object> data)
        {
            return Encode(output =>
            {
                foreach (KeyValuePair<string, object> kvp in data)
                {
                    byte[] value = EncodeStructValue(kvp.Value);
                    if (value == null)
                    {
                        Base.Compute.RecordWarning($"The entry '{kvp.Key}' holds a {kvp.Value.GetType().Name}, which cannot be written to a google.protobuf.Struct, and has been left out.");
                        continue;
                    }

                    WriteBytes(output, 1, Encode(entry =>
                    {
                        entry.WriteTag(1, WireFormat.WireType.LengthDelimited);
                        entry.WriteString(kvp.Key);
                        WriteBytes(entry, 2, value);
                    }));
                }
            });
        }

        /***************************************************/

        // google.protobuf.Value { oneof kind { NullValue null_value = 1; double number_value = 2; string string_value = 3;
        // bool bool_value = 4; Struct struct_value = 5; ListValue list_value = 6; } }. Null for values that cannot be written.
        private static byte[] EncodeStructValue(object value)
        {
            switch (value)
            {
                case null:
                    return Encode(output =>
                    {
                        output.WriteTag(1, WireFormat.WireType.Varint);
                        output.WriteEnum(0);
                    });
                case string text:
                    return Encode(output =>
                    {
                        output.WriteTag(3, WireFormat.WireType.LengthDelimited);
                        output.WriteString(text);
                    });
                case bool b:
                    return Encode(output =>
                    {
                        output.WriteTag(4, WireFormat.WireType.Varint);
                        output.WriteBool(b);
                    });
                case Guid _:
                case Enum _:
                    return Encode(output =>
                    {
                        output.WriteTag(3, WireFormat.WireType.LengthDelimited);
                        output.WriteString(value.ToString());
                    });
                case IDictionary<string, object> dictionary:
                    return Encode(output => WriteBytes(output, 5, EncodeStruct(dictionary)));
                case IEnumerable list:
                    List<byte[]> items = list.Cast<object>().Select(EncodeStructValue).ToList();
                    if (items.Any(x => x == null))
                        return null;
                    return Encode(output => WriteBytes(output, 6, Encode(inner => items.ForEach(item => WriteBytes(inner, 1, item)))));
            }

            if (value is IConvertible && value.GetType().IsPrimitive || value is decimal)
            {
                double number = System.Convert.ToDouble(value);
                return Encode(output =>
                {
                    output.WriteTag(2, WireFormat.WireType.Fixed64);
                    output.WriteDouble(number);
                });
            }

            return null;
        }

        /***************************************************/

        private static void WriteBytes(CodedOutputStream output, int number, byte[] bytes)
        {
            if (bytes == null)
                return;

            output.WriteTag(number, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(bytes));
        }

        /***************************************************/

        private static byte[] Encode(Action<CodedOutputStream> write)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                CodedOutputStream output = new CodedOutputStream(stream);
                write(output);
                output.Flush();
                return stream.ToArray();
            }
        }

        /***************************************************/
        /**** Helpers                                   ****/
        /***************************************************/

        internal static uint Tag(int number, WireFormat.WireType wireType) => WireFormat.MakeTag(number, wireType);

        internal static int TagSize(uint tag) => CodedOutputStream.ComputeRawVarint32Size(tag);

        /***************************************************/

        // A compiled getter of a property, e.g. Func<Bar, Node> for Bar.Start.
        public static Delegate Getter(Type owner, PropertyInfo property)
        {
            return Compile(owner, property.PropertyType, p => Expression.Property(p, property));
        }

        /***************************************************/

        private static Delegate Compile(Type input, Type output, Func<ParameterExpression, Expression> body)
        {
            ParameterExpression parameter = Expression.Parameter(input);
            return Expression.Lambda(typeof(Func<,>).MakeGenericType(input, output), body(parameter), parameter).Compile();
        }

        /***************************************************/

        private static ICodec New(Type codecType, params object[] arguments)
        {
            return (ICodec)Activator.CreateInstance(codecType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private static readonly ConcurrentDictionary<(ProtoSchema, Type, MessageDescriptor), IMessagePlan> m_Plans = new ConcurrentDictionary<(ProtoSchema, Type, MessageDescriptor), IMessagePlan>();

        private static readonly Dictionary<(ProtoSchema, Type, MessageDescriptor), IMessagePlan> m_Pending = new Dictionary<(ProtoSchema, Type, MessageDescriptor), IMessagePlan>();

        private static readonly object m_Lock = new object();

        private static int m_Depth = 0;

        private static readonly uint m_TypeUrlTag = Tag(1, WireFormat.WireType.LengthDelimited);

        private static readonly uint m_ValueTag = Tag(2, WireFormat.WireType.LengthDelimited);

        private static readonly int m_TypeUrlTagSize = TagSize(m_TypeUrlTag);

        private static readonly int m_ValueTagSize = TagSize(m_ValueTag);

        /***************************************************/
    }
}
