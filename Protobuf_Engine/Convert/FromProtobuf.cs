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
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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

        [Description("Converts Protocol Buffers bytes back to a BHoM object, following the generated .proto definitions. \n" +
            "Fields that are absent get the proto3 default (zero, empty or null), as for any other protobuf reader.")]
        [Input("bytes", "The protobuf encoded object.")]
        [Input("type", "The C# type of the encoded message.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the type is used.")]
        [Output("obj", "The BHoM object read from the bytes.")]
        public static IObject FromProtobuf(this byte[] bytes, Type type, string schemaFile = "")
        {
            if (bytes == null)
            {
                Base.Compute.RecordError("Cannot read an object from null protobuf bytes.");
                return null;
            }

            if (type == null)
            {
                Base.Compute.RecordError("The type of the encoded message is required to read protobuf bytes. Use FromProtobufAny for bytes written with ToProtobufAny.");
                return null;
            }

            ProtoSchema schema = ProtoSchema.Find(type, schemaFile);
            if (schema == null)
                return null;

            return ReadMessage(bytes, type, schema.MessageOf(type), schema) as IObject;
        }

        /***************************************************/

        [Description("Converts Protocol Buffers bytes holding a google.protobuf.Any back to a BHoM object. The type is taken from the type url of the Any, so it does not need to be known up front.")]
        [Input("bytes", "The protobuf encoded google.protobuf.Any, for example written by ToProtobufAny.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the message named by the type url is used.")]
        [Output("obj", "The BHoM object read from the bytes.")]
        public static IObject FromProtobufAny(this byte[] bytes, string schemaFile = "")
        {
            if (bytes == null)
            {
                Base.Compute.RecordError("Cannot read an object from null protobuf bytes.");
                return null;
            }

            string messageName = AnyMessageName(bytes);
            if (string.IsNullOrEmpty(messageName))
            {
                Base.Compute.RecordError("The bytes do not hold a google.protobuf.Any with a type url.");
                return null;
            }

            ProtoSchema schema = ProtoSchema.FindByMessage(messageName, schemaFile);
            if (schema == null)
            {
                Base.Compute.RecordError($"None of the schemas installed in {Query.SchemaFolder()} holds the protobuf message {messageName}.");
                return null;
            }

            object result = ReadAny(bytes, schema);
            if (result != null && !(result is IObject))
                Base.Compute.RecordWarning($"The google.protobuf.Any holds a {result.GetType().Name}, not a BHoM object.");

            return result as IObject;
        }

        /***************************************************/
        /**** Private Methods - Messages                ****/
        /***************************************************/

        private static object ReadMessage(byte[] bytes, Type type, MessageDescriptor message, ProtoSchema schema)
        {
            Dictionary<string, PropertyInfo> properties = ProtoTypes.PropertiesByName(type);
            Dictionary<int, (FieldDescriptor Field, PropertyInfo Property)> fields = schema.PropertyFields(message)
                .Where(kvp => properties.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Value.FieldNumber, kvp => (kvp.Value, properties[kvp.Key]));

            Dictionary<PropertyInfo, object> values = new Dictionary<PropertyInfo, object>();
            Dictionary<PropertyInfo, List<object>> repeated = new Dictionary<PropertyInfo, List<object>>();

            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (!fields.TryGetValue(WireFormat.GetTagFieldNumber(tag), out (FieldDescriptor Field, PropertyInfo Property) field))
                {
                    input.SkipLastField(); // Unknown field, e.g. written with a newer version of the definitions.
                    continue;
                }

                Type propertyType = Nullable.GetUnderlyingType(field.Property.PropertyType) ?? field.Property.PropertyType;
                if (IsRepeated(propertyType))
                {
                    if (!repeated.TryGetValue(field.Property, out List<object> items))
                        repeated[field.Property] = items = new List<object>();
                    ReadRepeated(input, WireFormat.GetTagWireType(tag), field.Field, propertyType, schema, items);
                }
                else
                {
                    values[field.Property] = ReadSingle(input, WireFormat.GetTagWireType(tag), field.Field, propertyType, schema);
                }
            }

            foreach (KeyValuePair<PropertyInfo, List<object>> kvp in repeated)
                values[kvp.Key] = ToCollection(kvp.Key.PropertyType, kvp.Value);

            // proto3: an absent field means the default value, which may differ from the C# default of the property.
            foreach (PropertyInfo property in fields.Values.Select(f => f.Property).Where(p => !values.ContainsKey(p)))
            {
                if (ProtocolDefault(property.PropertyType, out object value))
                    values[property] = value;
            }

            return Instantiate(type, values);
        }

        /***************************************************/

        private static bool IsRepeated(Type type)
        {
            return type.FullName == ProtoTypes.FragmentSetName || ProtoTypes.IsDictionary(type, out _, out _) || ProtoTypes.IsCollection(type, out _);
        }

        /***************************************************/

        private static void ReadRepeated(CodedInputStream input, WireFormat.WireType wireType, FieldDescriptor field, Type type, ProtoSchema schema, List<object> items)
        {
            if (type.FullName == ProtoTypes.FragmentSetName)
            {
                object fragment = ReadUnion(input.ReadBytes().ToByteArray(), field.MessageType, schema);
                if (fragment != null)
                    items.Add(fragment);
                return;
            }

            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
            {
                FieldDescriptor keyField = field.MessageType.FindFieldByNumber(1);
                FieldDescriptor valueField = field.MessageType.FindFieldByNumber(2);
                object key = null;
                object value = null;
                List<object> valueItems = null;
                CodedInputStream entry = new CodedInputStream(input.ReadBytes().ToByteArray());
                uint tag;
                while ((tag = entry.ReadTag()) != 0)
                {
                    WireFormat.WireType entryWireType = WireFormat.GetTagWireType(tag);
                    int number = WireFormat.GetTagFieldNumber(tag);
                    if (number == 1)
                        key = ReadSingle(entry, entryWireType, keyField, keyType, schema);
                    else if (number == 2 && IsRepeated(valueType))
                        ReadRepeated(entry, entryWireType, valueField, valueType, schema, valueItems = valueItems ?? new List<object>());
                    else if (number == 2)
                        value = ReadSingle(entry, entryWireType, valueField, valueType, schema);
                    else
                        entry.SkipLastField();
                }

                if (valueItems != null)
                    value = ToCollection(valueType, valueItems);
                else if (value == null && ProtocolDefault(valueType, out object defaultValue))
                    value = defaultValue;

                if (key == null)
                    ProtocolDefault(keyType, out key);

                items.Add(new KeyValuePair<object, object>(key, value));
                return;
            }

            ProtoTypes.IsCollection(type, out Type itemType);
            if (ProtoTypes.IsCollection(itemType, out _))
            {
                // Nested collection, wrapped in a message { repeated items = 1; }.
                FieldDescriptor innerField = field.MessageType.FindFieldByNumber(1);
                List<object> inner = new List<object>();
                CodedInputStream wrapper = new CodedInputStream(input.ReadBytes().ToByteArray());
                uint tag;
                while ((tag = wrapper.ReadTag()) != 0)
                {
                    if (WireFormat.GetTagFieldNumber(tag) == 1)
                        ReadRepeated(wrapper, WireFormat.GetTagWireType(tag), innerField, itemType, schema, inner);
                    else
                        wrapper.SkipLastField();
                }
                items.Add(ToCollection(itemType, inner));
                return;
            }

            if (wireType == WireFormat.WireType.LengthDelimited && ProtoTypes.IsPackable(itemType))
            {
                // Packed repeated scalars: the default encoding of other protobuf writers.
                CodedInputStream packed = new CodedInputStream(input.ReadBytes().ToByteArray());
                while (!packed.IsAtEnd)
                {
                    if (TryReadScalar(packed, itemType, out object item))
                        items.Add(item);
                }
                return;
            }

            items.Add(ReadSingle(input, wireType, field, itemType, schema));
        }

        /***************************************************/

        private static object ReadSingle(CodedInputStream input, WireFormat.WireType wireType, FieldDescriptor field, Type type, ProtoSchema schema)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (ProtoTypes.IsAnyValue(type))
                return ReadAny(input.ReadBytes().ToByteArray(), schema);

            if (TryReadScalar(input, type, out object value))
                return value;

            if (ProtoTypes.IsTuple(type))
                return ReadTuple(input.ReadBytes().ToByteArray(), field.MessageType, type, schema);

            if (field == null || field.FieldType != FieldType.Message)
            {
                input.SkipLastField();
                return null;
            }

            MessageDescriptor message = field.MessageType;
            byte[] bytes = input.ReadBytes().ToByteArray();
            if (ProtoSchema.IsAny(message))
                return ReadAny(bytes, schema);
            if (ProtoSchema.IsUnion(message))
                return ReadUnion(bytes, message, schema);

            return ReadMessage(bytes, type, message, schema);
        }

        /***************************************************/

        // Values with a fixed encoding that does not depend on the schema. Returns false for messages.
        private static bool TryReadScalar(CodedInputStream input, Type type, out object value)
        {
            value = null;
            if (type == typeof(string)) value = input.ReadString();
            else if (type == typeof(Guid)) value = Guid.TryParse(input.ReadString(), out Guid guid) ? guid : Guid.Empty;
            else if (type == typeof(char)) { string text = input.ReadString(); value = text.Length > 0 ? text[0] : default(char); }
            else if (type == typeof(double)) value = input.ReadDouble();
            else if (type == typeof(decimal)) value = (decimal)input.ReadDouble();
            else if (type == typeof(float)) value = input.ReadFloat();
            else if (type == typeof(int)) value = input.ReadInt32();
            else if (type == typeof(short)) value = (short)input.ReadInt32();
            else if (type == typeof(byte)) value = (byte)input.ReadInt32();
            else if (type == typeof(sbyte)) value = (sbyte)input.ReadInt32();
            else if (type == typeof(long)) value = input.ReadInt64();
            else if (type == typeof(uint)) value = input.ReadUInt32();
            else if (type == typeof(ushort)) value = (ushort)input.ReadUInt32();
            else if (type == typeof(ulong)) value = input.ReadUInt64();
            else if (type == typeof(bool)) value = input.ReadBool();
            else if (type == typeof(DateTime)) value = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ReadSecondsAndNanos(input.ReadBytes().ToByteArray()));
            else if (type == typeof(DateTimeOffset)) value = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ReadSecondsAndNanos(input.ReadBytes().ToByteArray())));
            else if (type == typeof(TimeSpan)) value = new TimeSpan(ReadSecondsAndNanos(input.ReadBytes().ToByteArray()));
            else if (type == typeof(Type)) value = ProtoTypes.ResolveType(input.ReadString());
            else if (type.FullName == "System.Drawing.Color") value = type.GetMethod("FromArgb", new[] { typeof(int) }).Invoke(null, new object[] { unchecked((int)input.ReadFixed32()) });
            else if (type.IsEnum && ProtoTypes.IsOmType(type)) value = Enum.ToObject(type, input.ReadEnum());
            else if (type.IsEnum)
            {
                string name = input.ReadString();
                value = Enum.IsDefined(type, name) ? Enum.Parse(type, name) : Activator.CreateInstance(type);
            }
            else
                return false;

            return true;
        }

        /***************************************************/

        private static object ReadTuple(byte[] bytes, MessageDescriptor message, Type type, ProtoSchema schema)
        {
            Type[] itemTypes = type.GetGenericArguments();
            object[] items = new object[itemTypes.Length];
            List<object>[] repeatedItems = new List<object>[itemTypes.Length];

            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int index = WireFormat.GetTagFieldNumber(tag) - 1;
                if (index < 0 || index >= itemTypes.Length)
                {
                    input.SkipLastField();
                    continue;
                }

                FieldDescriptor field = message.FindFieldByNumber(index + 1);
                if (IsRepeated(itemTypes[index]))
                    ReadRepeated(input, WireFormat.GetTagWireType(tag), field, itemTypes[index], schema, repeatedItems[index] = repeatedItems[index] ?? new List<object>());
                else
                    items[index] = ReadSingle(input, WireFormat.GetTagWireType(tag), field, itemTypes[index], schema);
            }

            for (int i = 0; i < itemTypes.Length; i++)
            {
                if (repeatedItems[i] != null)
                    items[i] = ToCollection(itemTypes[i], repeatedItems[i]);
                else if (items[i] == null && ProtocolDefault(itemTypes[i], out object value))
                    items[i] = value;
            }

            return Activator.CreateInstance(type, items);
        }

        /***************************************************/
        /**** Private Methods - Unions and Any          ****/
        /***************************************************/

        private static object ReadUnion(byte[] bytes, MessageDescriptor union, ProtoSchema schema)
        {
            if (ProtoSchema.IsAny(union))
                return ReadAny(bytes, schema);

            object result = null;
            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                FieldDescriptor field = union.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
                if (field == null || field.FieldType != FieldType.Message)
                {
                    Base.Compute.RecordWarning($"Field {WireFormat.GetTagFieldNumber(tag)} of the union {union.FullName} is not defined and has been ignored.");
                    input.SkipLastField();
                    continue;
                }

                byte[] content = input.ReadBytes().ToByteArray();
                if (ProtoSchema.IsAny(field.MessageType))
                {
                    result = ReadAny(content, schema);
                    continue;
                }

                Type concrete = schema.TypeOf(field.MessageType);
                if (concrete == null)
                {
                    Base.Compute.RecordWarning($"The C# type of {field.MessageType.FullName} could not be found, and the value has been ignored.");
                    continue;
                }

                result = ReadMessage(content, concrete, field.MessageType, schema);
            }

            return result;
        }

        /***************************************************/

        private static object ReadAny(byte[] bytes, ProtoSchema schema)
        {
            SplitAny(bytes, out string typeUrl, out byte[] content);
            string messageName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
            if (messageName == "google.protobuf.Struct")
                return ReadStruct(content);

            if (messageName == "google.protobuf.ListValue")
                return ReadListValue(content);

            Type wrapped = WrapperType(messageName);
            if (wrapped != null)
            {
                CodedInputStream wrapper = new CodedInputStream(content);
                if (wrapper.ReadTag() == 0)
                    return wrapped == typeof(string) ? "" : Activator.CreateInstance(wrapped); // Absent value field: the proto3 default.

                TryReadScalar(wrapper, wrapped, out object value);
                return value;
            }

            // The message may be defined in another installed schema, e.g. of another toolkit.
            ProtoSchema owner = schema.MessagesByName.ContainsKey(messageName) ? schema : ProtoSchema.FindByMessage(messageName, "");
            MessageDescriptor message = null;
            owner?.MessagesByName.TryGetValue(messageName, out message);
            Type type = message == null ? null : owner.TypeOf(message);
            if (type == null)
            {
                Base.Compute.RecordWarning($"The google.protobuf.Any type '{typeUrl}' does not match a known type and has been ignored.");
                return null;
            }

            return ReadMessage(content, type, message, owner);
        }

        /***************************************************/

        // google.protobuf.Any { string type_url = 1; bytes value = 2; }
        private static void SplitAny(byte[] bytes, out string typeUrl, out byte[] content)
        {
            typeUrl = "";
            content = new byte[0];
            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (WireFormat.GetTagFieldNumber(tag))
                {
                    case 1:
                        typeUrl = input.ReadString();
                        break;
                    case 2:
                        content = input.ReadBytes().ToByteArray();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
        }

        /***************************************************/

        // The protobuf message named by the type url of an Any, e.g. bhom.disciplines.Bar.
        private static string AnyMessageName(byte[] bytes)
        {
            try
            {
                SplitAny(bytes, out string typeUrl, out _);
                return typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
            }
            catch (InvalidProtocolBufferException)
            {
                return null;
            }
        }

        /***************************************************/

        private static Type WrapperType(string protoName)
        {
            switch (protoName)
            {
                case "google.protobuf.DoubleValue": return typeof(double);
                case "google.protobuf.FloatValue": return typeof(float);
                case "google.protobuf.Int32Value": return typeof(int);
                case "google.protobuf.Int64Value": return typeof(long);
                case "google.protobuf.UInt32Value": return typeof(uint);
                case "google.protobuf.UInt64Value": return typeof(ulong);
                case "google.protobuf.BoolValue": return typeof(bool);
                case "google.protobuf.StringValue": return typeof(string);
                default: return null;
            }
        }

        /***************************************************/
        /**** Private Methods - Well known types        ****/
        /***************************************************/

        private static Dictionary<string, object> ReadStruct(byte[] bytes)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) != 1)
                {
                    input.SkipLastField();
                    continue;
                }

                string key = "";
                object value = null;
                CodedInputStream entry = new CodedInputStream(input.ReadBytes().ToByteArray());
                uint entryTag;
                while ((entryTag = entry.ReadTag()) != 0)
                {
                    int number = WireFormat.GetTagFieldNumber(entryTag);
                    if (number == 1)
                        key = entry.ReadString();
                    else if (number == 2)
                        value = ReadStructValue(entry.ReadBytes().ToByteArray());
                    else
                        entry.SkipLastField();
                }
                result[key] = value;
            }
            return result;
        }

        /***************************************************/

        private static object ReadStructValue(byte[] bytes)
        {
            object result = null;
            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (WireFormat.GetTagFieldNumber(tag))
                {
                    case 1: input.ReadEnum(); result = null; break;
                    case 2: result = input.ReadDouble(); break;
                    case 3: result = input.ReadString(); break;
                    case 4: result = input.ReadBool(); break;
                    case 5: result = ReadStruct(input.ReadBytes().ToByteArray()); break;
                    case 6: result = ReadListValue(input.ReadBytes().ToByteArray()); break;
                    default: input.SkipLastField(); break;
                }
            }
            return result;
        }

        /***************************************************/

        // google.protobuf.ListValue { repeated Value values = 1; }
        private static List<object> ReadListValue(byte[] bytes)
        {
            List<object> list = new List<object>();
            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) == 1)
                    list.Add(ReadStructValue(input.ReadBytes().ToByteArray()));
                else
                    input.SkipLastField();
            }
            return list;
        }

        /***************************************************/

        private static long ReadSecondsAndNanos(byte[] bytes)
        {
            long seconds = 0;
            int nanos = 0;
            CodedInputStream input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (WireFormat.GetTagFieldNumber(tag))
                {
                    case 1: seconds = input.ReadInt64(); break;
                    case 2: nanos = input.ReadInt32(); break;
                    default: input.SkipLastField(); break;
                }
            }
            return seconds * TimeSpan.TicksPerSecond + nanos / 100;
        }

        /***************************************************/
        /**** Private Methods - Object construction     ****/
        /***************************************************/

        // The proto3 default of an absent field. Returns false when the C# default of the property should be kept.
        private static bool ProtocolDefault(Type type, out object value)
        {
            value = null;
            if (Nullable.GetUnderlyingType(type) != null)
                return true;

            if (type == typeof(string))
            {
                value = "";
                return true;
            }

            if (type.IsEnum)
            {
                // Keep the C# default when the enum has no value 0, as the generated UNSPECIFIED value has no C# equivalent.
                if (!Enum.IsDefined(type, Enum.ToObject(type, 0)))
                    return false;
                value = Enum.ToObject(type, 0);
                return true;
            }

            if (type.IsPrimitive || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            {
                value = Activator.CreateInstance(type);
                return true;
            }

            if (type.IsValueType)
                return false; // Other structs, e.g. System.Drawing.Color: keep the C# default.

            if (type.FullName == ProtoTypes.FragmentSetName || ProtoTypes.IsDictionary(type, out _, out _) || ProtoTypes.IsCollection(type, out _))
            {
                value = ToCollection(type, new List<object>());
                return true;
            }

            return true; // Messages, unions and Any: null.
        }

        /***************************************************/

        private static object ToCollection(Type type, List<object> items)
        {
            if (type.FullName == ProtoTypes.FragmentSetName)
            {
                IList fragments = (IList)Activator.CreateInstance(type);
                foreach (object fragment in items)
                    fragments.Add(fragment);
                return fragments;
            }

            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
            {
                Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                IDictionary dictionary = (IDictionary)Activator.CreateInstance(type.IsInterface || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null ? dictionaryType : type);
                foreach (KeyValuePair<object, object> kvp in items.OfType<KeyValuePair<object, object>>())
                {
                    if (kvp.Key != null)
                        dictionary[kvp.Key] = kvp.Value;
                }

                if (type.IsInstanceOfType(dictionary))
                    return dictionary;

                // Read-only dictionaries, e.g. ReadOnlyDictionary<TKey, TValue>: construct from a dictionary.
                ConstructorInfo fromDictionary = type.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.IsAssignableFrom(dictionaryType));
                return fromDictionary?.Invoke(new object[] { dictionary });
            }

            ProtoTypes.IsCollection(type, out Type itemType);
            if (ProtoTypes.IsTwoDimensionalArray(type))
            {
                List<Array> rows = items.OfType<Array>().ToList();
                Array matrix = Array.CreateInstance(type.GetElementType(), rows.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Length));
                for (int i = 0; i < rows.Count; i++)
                {
                    for (int j = 0; j < rows[i].Length; j++)
                        matrix.SetValue(rows[i].GetValue(j), i, j);
                }
                return matrix;
            }

            if (type.IsArray)
            {
                Array array = Array.CreateInstance(itemType, items.Count);
                for (int i = 0; i < items.Count; i++)
                    array.SetValue(items[i], i);
                return array;
            }

            Type listType = typeof(List<>).MakeGenericType(itemType);
            IList typedItems = (IList)Activator.CreateInstance(listType);
            foreach (object item in items)
                typedItems.Add(item);

            if (type.IsAssignableFrom(listType))
                return typedItems;

            // Read-only collections, e.g. ReadOnlyCollection<T>: construct from the list.
            ConstructorInfo fromList = type.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.IsAssignableFrom(listType));
            if (fromList != null && type.GetConstructor(Type.EmptyTypes) == null)
                return fromList.Invoke(new object[] { typedItems });

            Type collectionType = type;
            object collection = Activator.CreateInstance(collectionType);
            if (collection is IList list)
            {
                foreach (object item in items)
                    list.Add(item);
            }
            else
            {
                MethodInfo add = collectionType.GetMethod("Add", new[] { itemType });
                foreach (object item in items)
                    add?.Invoke(collection, new[] { item });
            }
            return collection;
        }

        /***************************************************/

        private static object Instantiate(Type type, Dictionary<PropertyInfo, object> values)
        {
            object result;
            List<PropertyInfo> remaining = values.Keys.ToList();

            if (type.GetConstructor(Type.EmptyTypes) != null)
            {
                result = Activator.CreateInstance(type);
            }
            else
            {
                // Immutable objects: use the constructor whose parameters match the properties.
                ConstructorInfo constructor = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault(c => c.GetParameters().All(p => values.Keys.Any(v => string.Equals(v.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                        || type.GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null));

                if (constructor != null)
                {
                    object[] arguments = constructor.GetParameters().Select(p =>
                    {
                        PropertyInfo match = values.Keys.FirstOrDefault(v => string.Equals(v.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            remaining.Remove(match);
                            return values[match];
                        }
                        return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                    }).ToArray();
                    result = constructor.Invoke(arguments);
                }
                else
                {
                    result = FormatterServices.GetUninitializedObject(type);
                }
            }

            foreach (PropertyInfo property in remaining)
                SetValue(result, property, values[property]);

            return result;
        }

        /***************************************************/

        private static void SetValue(object obj, PropertyInfo property, object value)
        {
            if (value != null && !property.PropertyType.IsInstanceOfType(value))
            {
                Type target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                try
                {
                    value = target.IsEnum ? Enum.ToObject(target, value) : System.Convert.ChangeType(value, target);
                }
                catch
                {
                    Base.Compute.RecordWarning($"Could not set {property.DeclaringType.Name}.{property.Name} from a value of type {value.GetType().Name}.");
                    return;
                }
            }

            MethodInfo setter = property.GetSetMethod(true);
            if (setter != null)
            {
                setter.Invoke(obj, new[] { value });
                return;
            }

            // Get-only auto properties: set the compiler generated backing field.
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return;
                }
            }
        }

        /***************************************************/
    }
}
