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
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using BH.oM.Base;
using BH.oM.Base.Attributes;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf
{
    public static partial class Convert
    {
        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        [Description("Converts a BHoM object to Protocol Buffers bytes, following the generated .proto definitions. \n" +
            "The bytes do not say which message they hold: the reader needs to know the type, for example from the gRPC service definition. Use ToProtobufAny to include the type.")]
        [Input("obj", "The object to convert. Its type must be part of the generated definitions.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the type of the object is used.")]
        [Output("bytes", "The protobuf encoded object, readable by any client generated from the .proto files.")]
        public static byte[] ToProtobuf(this IObject obj, string schemaFile = "")
        {
            if (obj == null)
            {
                Base.Compute.RecordError("Cannot convert a null object to protobuf.");
                return null;
            }

            ProtoSchema schema = ProtoSchema.Find(obj.GetType(), schemaFile);
            if (schema == null)
                return null;

            return WriteMessage(obj, obj.GetType(), schema.MessageOf(obj.GetType()), schema);
        }

        /***************************************************/

        [Description("Converts a BHoM object to Protocol Buffers bytes wrapped in a google.protobuf.Any, whose type url names the message. \n" +
            "This allows the object to be read back without knowing its type up front.")]
        [Input("obj", "The object to convert. Its type must be part of the generated definitions.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the type of the object is used.")]
        [Output("bytes", "The protobuf encoded google.protobuf.Any holding the object.")]
        public static byte[] ToProtobufAny(this IObject obj, string schemaFile = "")
        {
            if (obj == null)
            {
                Base.Compute.RecordError("Cannot convert a null object to protobuf.");
                return null;
            }

            ProtoSchema schema = ProtoSchema.Find(obj.GetType(), schemaFile);
            if (schema == null)
                return null;

            return WriteAny(obj, schema);
        }

        /***************************************************/
        /**** Private Methods - Messages                ****/
        /***************************************************/

        private static byte[] WriteMessage(object obj, Type type, MessageDescriptor message, ProtoSchema schema)
        {
            Dictionary<string, PropertyInfo> properties = ProtoTypes.PropertiesByName(type);
            return Encode(output =>
            {
                foreach (KeyValuePair<string, FieldDescriptor> kvp in schema.PropertyFields(message))
                {
                    if (properties.TryGetValue(kvp.Key, out PropertyInfo property))
                        WriteField(output, kvp.Value, property.PropertyType, property.GetValue(obj), schema, false);
                }
            });
        }

        /***************************************************/

        private static void WriteField(CodedOutputStream output, FieldDescriptor field, Type type, object value, ProtoSchema schema, bool force)
        {
            if (value == null)
                return;

            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
            {
                // proto3 'optional': an explicitly set value is always written, even when it is the default.
                type = nullable;
                force = true;
            }

            if (type.FullName == ProtoTypes.FragmentSetName)
            {
                foreach (object fragment in (IEnumerable)value)
                {
                    if (fragment != null)
                        WriteBytes(output, field.FieldNumber, WriteUnion(fragment, field.MessageType, schema));
                }
                return;
            }

            if (ProtoTypes.IsTwoDimensionalArray(type))
            {
                // Written as rows, each wrapped in a message { repeated items = 1; }.
                FieldDescriptor items = field.MessageType.FindFieldByNumber(1);
                Array array = (Array)value;
                for (int i = 0; i < array.GetLength(0); i++)
                {
                    Array row = Array.CreateInstance(type.GetElementType(), array.GetLength(1));
                    for (int j = 0; j < array.GetLength(1); j++)
                        row.SetValue(array.GetValue(i, j), j);
                    WriteBytes(output, field.FieldNumber, Encode(inner => WriteField(inner, items, row.GetType(), row, schema, true)));
                }
                return;
            }

            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
            {
                // Both map<,> entries and the key/value wrapper messages are messages {1: key, 2: value}.
                FieldDescriptor keyField = field.MessageType.FindFieldByNumber(1);
                FieldDescriptor valueField = field.MessageType.FindFieldByNumber(2);
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    WriteBytes(output, field.FieldNumber, Encode(inner =>
                    {
                        WriteField(inner, keyField, keyType, entry.Key, schema, true);
                        WriteField(inner, valueField, valueType, entry.Value, schema, true);
                    }));
                }
                return;
            }

            if (ProtoTypes.IsCollection(type, out Type itemType))
            {
                bool nested = ProtoTypes.IsCollection(itemType, out _);
                FieldDescriptor items = nested ? field.MessageType.FindFieldByNumber(1) : field;
                foreach (object item in (IEnumerable)value)
                {
                    if (item == null)
                        continue; // Protobuf cannot represent null items in a repeated field.

                    if (nested)
                        WriteBytes(output, field.FieldNumber, Encode(inner => WriteField(inner, items, itemType, item, schema, true)));
                    else
                        WriteField(output, field, itemType, item, schema, true);
                }
                return;
            }

            WriteSingle(output, field, type, value, schema, force);
        }

        /***************************************************/

        private static void WriteSingle(CodedOutputStream output, FieldDescriptor field, Type type, object value, ProtoSchema schema, bool force)
        {
            if (ProtoTypes.IsAnyValue(type))
            {
                WriteBytes(output, field.FieldNumber, WriteAny(value, schema));
                return;
            }

            if (ProtoTypes.IsTuple(type))
            {
                Type[] itemTypes = type.GetGenericArguments();
                WriteBytes(output, field.FieldNumber, Encode(inner =>
                {
                    for (int i = 0; i < itemTypes.Length; i++)
                        WriteField(inner, field.MessageType.FindFieldByNumber(i + 1), itemTypes[i], type.GetProperty($"Item{i + 1}").GetValue(value), schema, true);
                }));
                return;
            }

            if (WriteScalar(output, field.FieldNumber, type, value, force))
                return;

            if (field.FieldType != FieldType.Message)
            {
                Base.Compute.RecordWarning($"A value of type {type.Name} does not match the field {field.FullName} and has been left out.");
                return;
            }

            MessageDescriptor message = field.MessageType;
            if (ProtoSchema.IsAny(message))
                WriteBytes(output, field.FieldNumber, WriteAny(value, schema));
            else if (ProtoSchema.IsUnion(message))
                WriteBytes(output, field.FieldNumber, WriteUnion(value, message, schema));
            else
                WriteBytes(output, field.FieldNumber, WriteMessage(value, type, message, schema));
        }

        /***************************************************/

        // Values with a fixed encoding that does not depend on the schema. Returns false for messages.
        private static bool WriteScalar(CodedOutputStream output, int number, Type type, object value, bool force)
        {
            switch (value)
            {
                case string text:
                    if (force || text.Length > 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.LengthDelimited);
                        output.WriteString(text);
                    }
                    return true;
                case Guid guid:
                    output.WriteTag(number, WireFormat.WireType.LengthDelimited);
                    output.WriteString(guid.ToString());
                    return true;
                case char character:
                    output.WriteTag(number, WireFormat.WireType.LengthDelimited);
                    output.WriteString(character.ToString());
                    return true;
                case double d:
                    WriteDouble(output, number, d, force);
                    return true;
                case decimal m:
                    WriteDouble(output, number, (double)m, force);
                    return true;
                case float f:
                    if (force || f != 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.Fixed32);
                        output.WriteFloat(f);
                    }
                    return true;
                case bool b:
                    if (force || b)
                    {
                        output.WriteTag(number, WireFormat.WireType.Varint);
                        output.WriteBool(b);
                    }
                    return true;
                case int _:
                case short _:
                case byte _:
                case sbyte _:
                    int i = System.Convert.ToInt32(value);
                    if (force || i != 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.Varint);
                        output.WriteInt32(i);
                    }
                    return true;
                case long l:
                    if (force || l != 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.Varint);
                        output.WriteInt64(l);
                    }
                    return true;
                case uint _:
                case ushort _:
                    uint u = System.Convert.ToUInt32(value);
                    if (force || u != 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.Varint);
                        output.WriteUInt32(u);
                    }
                    return true;
                case ulong ul:
                    if (force || ul != 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.Varint);
                        output.WriteUInt64(ul);
                    }
                    return true;
                case DateTime dateTime:
                    WriteBytes(output, number, WriteTimestamp(dateTime));
                    return true;
                case DateTimeOffset dateTimeOffset:
                    WriteBytes(output, number, WriteTimestamp(dateTimeOffset.UtcDateTime));
                    return true;
                case TimeSpan timeSpan:
                    WriteBytes(output, number, WriteDuration(timeSpan));
                    return true;
                case Type systemType:
                    output.WriteTag(number, WireFormat.WireType.LengthDelimited);
                    output.WriteString(systemType.FullName);
                    return true;
            }

            if (type.FullName == "System.Drawing.Color")
            {
                output.WriteTag(number, WireFormat.WireType.Fixed32);
                output.WriteFixed32(unchecked((uint)(int)value.GetType().GetMethod("ToArgb").Invoke(value, null)));
                return true;
            }

            if (type.IsEnum)
            {
                if (ProtoTypes.IsOmType(type))
                {
                    int e = System.Convert.ToInt32(value);
                    if (force || e != 0)
                    {
                        output.WriteTag(number, WireFormat.WireType.Varint);
                        output.WriteEnum(e);
                    }
                }
                else
                {
                    output.WriteTag(number, WireFormat.WireType.LengthDelimited);
                    output.WriteString(value.ToString());
                }
                return true;
            }

            return false;
        }

        /***************************************************/
        /**** Private Methods - Unions and Any          ****/
        /***************************************************/

        private static byte[] WriteUnion(object value, MessageDescriptor union, ProtoSchema schema)
        {
            if (ProtoSchema.IsAny(union))
                return WriteAny(value, schema);

            FieldDescriptor slot = schema.UnionSlot(union, value.GetType().FullName);
            if (slot != null)
                return Encode(output => WriteBytes(output, slot.FieldNumber, WriteMessage(value, value.GetType(), slot.MessageType, schema)));

            FieldDescriptor other = union.FindFieldByNumber(ProtoTypes.OtherFieldNumber);
            if (other != null)
            {
                byte[] any = WriteAny(value, schema);
                return any == null ? null : Encode(output => WriteBytes(output, other.FieldNumber, any));
            }

            Base.Compute.RecordError($"{value.GetType().FullName} has no slot in the union {union.FullName}.");
            return null;
        }

        /***************************************************/

        // google.protobuf.Any { string type_url = 1; bytes value = 2; }. The type url plays the role of the "_t" of BHoM json.
        private static byte[] WriteAny(object value, ProtoSchema schema)
        {
            string typeName;
            byte[] content;

            ProtoSchema owner = schema.MessageOf(value.GetType()) != null ? schema : ProtoSchema.Find(value.GetType(), "", true);
            if (owner != null)
            {
                MessageDescriptor message = owner.MessageOf(value.GetType());
                typeName = message.FullName;
                content = WriteMessage(value, value.GetType(), message, owner);
            }
            else if (value is IDictionary<string, object> dictionary)
            {
                typeName = "google.protobuf.Struct";
                content = WriteStruct(dictionary);
            }
            else if (value is IEnumerable list && !(value is string))
            {
                // google.protobuf.ListValue { repeated Value values = 1; }
                List<byte[]> items = list.Cast<object>().Select(WriteStructValue).ToList();
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
                content = Encode(output => WriteScalar(output, 1, value.GetType(), value, true));
            }

            return Encode(output =>
            {
                output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                output.WriteString(ProtoTypes.TypeUrlPrefix + typeName);
                WriteBytes(output, 2, content);
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
        /**** Private Methods - Well known types        ****/
        /***************************************************/

        // google.protobuf.Struct { map<string, Value> fields = 1; }
        private static byte[] WriteStruct(IDictionary<string, object> data)
        {
            return Encode(output =>
            {
                foreach (KeyValuePair<string, object> kvp in data)
                {
                    byte[] value = WriteStructValue(kvp.Value);
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

        // google.protobuf.Value { oneof kind { NullValue null_value = 1; double number_value = 2; string string_value = 3; bool bool_value = 4; Struct struct_value = 5; ListValue list_value = 6; } }
        private static byte[] WriteStructValue(object value)
        {
            switch (value)
            {
                case null:
                    return Encode(output => { output.WriteTag(1, WireFormat.WireType.Varint); output.WriteEnum(0); });
                case string text:
                    return Encode(output => { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteString(text); });
                case bool b:
                    return Encode(output => { output.WriteTag(4, WireFormat.WireType.Varint); output.WriteBool(b); });
                case Guid _:
                case Enum _:
                    return Encode(output => { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteString(value.ToString()); });
                case IDictionary<string, object> dictionary:
                    return Encode(output => WriteBytes(output, 5, WriteStruct(dictionary)));
                case IEnumerable list:
                    List<byte[]> items = list.Cast<object>().Select(WriteStructValue).ToList();
                    if (items.Any(x => x == null))
                        return null;
                    return Encode(output => WriteBytes(output, 6, Encode(inner => items.ForEach(item => WriteBytes(inner, 1, item)))));
            }

            if (value is IConvertible && value.GetType().IsPrimitive || value is decimal)
            {
                double number = System.Convert.ToDouble(value);
                return Encode(output => { output.WriteTag(2, WireFormat.WireType.Fixed64); output.WriteDouble(number); });
            }

            return null;
        }

        /***************************************************/

        // google.protobuf.Timestamp { int64 seconds = 1; int32 nanos = 2; }
        private static byte[] WriteTimestamp(DateTime dateTime)
        {
            long ticks = dateTime.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            return WriteSecondsAndNanos(ticks);
        }

        /***************************************************/

        // google.protobuf.Duration { int64 seconds = 1; int32 nanos = 2; }
        private static byte[] WriteDuration(TimeSpan timeSpan)
        {
            return WriteSecondsAndNanos(timeSpan.Ticks);
        }

        /***************************************************/

        private static byte[] WriteSecondsAndNanos(long ticks)
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
        /**** Private Methods - Encoding helpers        ****/
        /***************************************************/

        private static void WriteDouble(CodedOutputStream output, int number, double value, bool force)
        {
            // NaN is not equal to 0, so it is always written.
            if (force || value != 0)
            {
                output.WriteTag(number, WireFormat.WireType.Fixed64);
                output.WriteDouble(value);
            }
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
    }
}
