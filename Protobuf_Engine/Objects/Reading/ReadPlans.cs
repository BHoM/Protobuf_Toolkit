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
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BH.oM.Base;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    // Compiled reader: the same results as the reflection reference reader of the BHoM_Proto tests, with every decision
    // about a C# type taken once, when its plan is built:
    // - A ReadPlan<T> per (schema, C# type, message) maps field numbers to typed field readers, with value readers chosen
    //   once for the declared property type, compiled setters, typed collection builders and the proto3 defaults.
    // - Nested messages are read in place, within a length limit on the same input (PushLimit / PopLimit), instead of
    //   being copied into their own byte array and stream.
    // - Types with a public parameterless constructor are created first and their properties set as their fields are read.
    //   Other types (e.g. immutable ones) collect the values first and are built with their constructor.
    internal static class ReadPlans
    {
        /***************************************************/
        /**** Reading                                   ****/
        /***************************************************/

        // A message of a known type, e.g. for FromProtobuf.
        public static object Read(IReadPlan plan, byte[] bytes)
        {
            return plan.ReadObject(new ProtoReader(bytes));
        }

        /***************************************************/

        // A google.protobuf.Any, e.g. for FromProtobufAny: reads up to the end of the input or its current limit.
        // The value is read with the plan of the message named by the type url when there is one, else as a well-known type.
        public static object ReadAny(ProtoReader input, ProtoSchema schema, Type expected)
        {
            string url = null;
            byte[] pending = null;
            object result = null;
            bool read = false;

            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (WireFormat.GetTagFieldNumber(tag))
                {
                    case 1:
                        url = input.ReadString();
                        break;
                    case 2:
                        IReadPlan plan = url == null ? null : AnyPlan(url, schema);
                        if (plan != null)
                        {
                            int limit = input.PushLimit(input.ReadLength());
                            result = plan.ReadObject(input);
                            input.PopLimit(limit);
                            read = true;
                        }
                        else
                        {
                            pending = input.ReadBytes();
                            read = false;
                        }
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }

            // Well-known types (wrappers, Struct, ListValue), unknown types, or a value written before its type url.
            if (!read)
                result = ReadAnyContent(url ?? "", pending ?? new byte[0], schema);

            return CheckType(result, expected);
        }

        /***************************************************/

        // A value that does not fit where it is read into is ignored with a warning.
        public static object CheckType(object value, Type expected)
        {
            if (value == null || expected == null)
                return value;

            expected = Nullable.GetUnderlyingType(expected) ?? expected;
            if (expected.IsInstanceOfType(value))
                return value;

            Base.Compute.RecordWarning($"A google.protobuf.Any holding a {value.GetType().FullName} cannot be read as a {expected.FullName} and has been ignored.");
            return null;
        }

        /***************************************************/
        /**** Plans                                     ****/
        /***************************************************/

        public static IReadPlan Get(ProtoSchema schema, Type type, MessageDescriptor message)
        {
            (ProtoSchema, Type, MessageDescriptor) key = (schema, type, message);
            if (m_Plans.TryGetValue(key, out IReadPlan plan))
                return plan;

            lock (m_Lock)
            {
                if (m_Plans.TryGetValue(key, out plan) || m_Pending.TryGetValue(key, out plan))
                    return plan;

                // As for the write plans: plans of types referring to each other get each other's plan before it is complete.
                plan = (IReadPlan)Activator.CreateInstance(typeof(ReadPlan<>).MakeGenericType(type), schema, message);
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
                        foreach (KeyValuePair<(ProtoSchema, Type, MessageDescriptor), IReadPlan> pending in m_Pending)
                            m_Plans[pending.Key] = pending.Value;
                        m_Pending.Clear();
                    }
                }
                return plan;
            }
        }

        /***************************************************/

        // The plan for the message named by a type url: in this schema, or else in a registered one. Null for well-known and unknown types.
        private static IReadPlan AnyPlan(string url, ProtoSchema schema)
        {
            return m_AnyPlans.GetOrAdd((schema, url), key =>
            {
                string name = url.Substring(url.LastIndexOf('/') + 1);
                ProtoSchema owner = schema.MessagesByName.ContainsKey(name) ? schema : ProtoSchema.FindByMessage(name, "");
                MessageDescriptor message = null;
                owner?.MessagesByName.TryGetValue(name, out message);
                Type type = message == null ? null : owner.TypeOf(message);
                return type == null ? null : Get(owner, type, message);
            });
        }

        /***************************************************/
        /**** Field readers                             ****/
        /***************************************************/

        // The reader of a property of T in a field.
        public static object CreateFieldReader(Type owner, PropertyInfo property, FieldDescriptor field, int index, ProtoSchema schema)
        {
            Type propertyType = property.PropertyType;
            Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            Delegate setter = Setter(owner, property);

            if (IsRepeated(type))
            {
                // The proto3 default of an absent repeated field: a new empty collection each time.
                Type itemType = ItemType(type);
                object itemReader = CreateItemReader(type, field, schema);
                object builder = CreateBuilder(type, itemType);
                Func<object> emptyCollection = (Func<object>)Generic(nameof(EmptyCollection), new object[] { builder }, type, itemType);
                return New(typeof(RepeatedFieldReader<,,>).MakeGenericType(owner, type, itemType), index, itemReader, builder, setter, emptyCollection);
            }

            object reader = CreateValueReader(propertyType, field, schema);
            return New(typeof(SingleFieldReader<,>).MakeGenericType(owner, propertyType), index, reader, setter, DefaultValue(propertyType));
        }

        /***************************************************/

        private static bool IsRepeated(Type type)
        {
            return type.FullName == ProtoTypes.FragmentSetName || ProtoTypes.IsDictionary(type, out _, out _) || ProtoTypes.IsCollection(type, out _);
        }

        /***************************************************/

        // The items collected for a repeated property: entries for dictionaries, items for collections.
        private static Type ItemType(Type type)
        {
            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
                return typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);

            ProtoTypes.IsCollection(type, out Type itemType);
            return itemType;
        }

        /***************************************************/

        // Reads the occurrences of a repeated field into a list.
        private static object CreateItemReader(Type type, FieldDescriptor field, ProtoSchema schema)
        {
            if (type.FullName != ProtoTypes.FragmentSetName && ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
            {
                FieldDescriptor keyField = field.MessageType.FindFieldByNumber(1);
                FieldDescriptor valueField = field.MessageType.FindFieldByNumber(2);
                object keyReader = CreateValueReader(keyType, keyField, schema);
                object keyDefault = ProtocolDefault(keyType, out object key) ? key : null;

                if (IsRepeated(valueType))
                {
                    Type valueItemType = ItemType(valueType);
                    return New(typeof(RepeatedMapEntryReader<,,>).MakeGenericType(keyType, valueType, valueItemType),
                        keyReader, keyDefault, CreateItemReader(valueType, valueField, schema), CreateBuilder(valueType, valueItemType));
                }

                object valueDefault = ProtocolDefault(valueType, out object value) ? value : null;
                return New(typeof(MapEntryReader<,>).MakeGenericType(keyType, valueType), keyReader, keyDefault, CreateValueReader(valueType, valueField, schema), valueDefault);
            }

            ProtoTypes.IsCollection(type, out Type itemType);
            if (type.FullName != ProtoTypes.FragmentSetName && ProtoTypes.IsCollection(itemType, out _))
            {
                // Nested collection, wrapped in a message { repeated items = 1; }.
                Type innerItemType = ItemType(itemType);
                FieldDescriptor innerField = field.MessageType.FindFieldByNumber(1);
                return New(typeof(NestedItemReader<,>).MakeGenericType(itemType, innerItemType), CreateItemReader(itemType, innerField, schema), CreateBuilder(itemType, innerItemType));
            }

            // FragmentSet: each fragment a google.protobuf.Any (or a union) read as an IFragment, as any other item.
            return New(typeof(SimpleItemReader<>).MakeGenericType(itemType), CreateValueReader(itemType, field, schema), ProtoTypes.IsPackable(itemType));
        }

        /***************************************************/

        // Reads one value of a declared type.
        public static object CreateValueReader(Type type, FieldDescriptor field, ProtoSchema schema)
        {
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                return New(typeof(NullableReader<>).MakeGenericType(nullable), CreateValueReader(nullable, field, schema));

            if (ProtoTypes.IsAnyValue(type))
                return New(typeof(AnyReader<>).MakeGenericType(type), schema);

            object scalar = ScalarReader(type);
            if (scalar != null)
                return scalar;

            if (ProtoTypes.IsTuple(type))
                return CreateTupleReader(type, field.MessageType, schema);

            if (field == null || field.FieldType != FieldType.Message)
                return New(typeof(MismatchReader<>).MakeGenericType(type));

            MessageDescriptor message = field.MessageType;
            if (ProtoSchema.IsAny(message))
                return New(typeof(AnyReader<>).MakeGenericType(type), schema);
            if (ProtoSchema.IsUnion(message))
                return New(typeof(UnionReader<>).MakeGenericType(type), message, schema);

            return New(typeof(MessageReader<>).MakeGenericType(type), Get(schema, type, message));
        }

        /***************************************************/

        private static object ScalarReader(Type type)
        {
            if (type == typeof(string)) return new ScalarReader<string>(i => i.ReadString());
            if (type == typeof(Guid)) return new ScalarReader<Guid>(i => Guid.TryParse(i.ReadString(), out Guid guid) ? guid : Guid.Empty);
            if (type == typeof(char)) return new ScalarReader<char>(i => { string text = i.ReadString(); return text.Length > 0 ? text[0] : default(char); });
            if (type == typeof(double)) return new ScalarReader<double>(i => i.ReadDouble());
            if (type == typeof(decimal)) return new ScalarReader<decimal>(i => (decimal)i.ReadDouble());
            if (type == typeof(float)) return new ScalarReader<float>(i => i.ReadFloat());
            if (type == typeof(int)) return new ScalarReader<int>(i => i.ReadInt32());
            if (type == typeof(short)) return new ScalarReader<short>(i => (short)i.ReadInt32());
            if (type == typeof(byte)) return new ScalarReader<byte>(i => (byte)i.ReadInt32());
            if (type == typeof(sbyte)) return new ScalarReader<sbyte>(i => (sbyte)i.ReadInt32());
            if (type == typeof(long)) return new ScalarReader<long>(i => i.ReadInt64());
            if (type == typeof(uint)) return new ScalarReader<uint>(i => i.ReadUInt32());
            if (type == typeof(ushort)) return new ScalarReader<ushort>(i => (ushort)i.ReadUInt32());
            if (type == typeof(ulong)) return new ScalarReader<ulong>(i => i.ReadUInt64());
            if (type == typeof(bool)) return new ScalarReader<bool>(i => i.ReadBool());
            if (type == typeof(DateTime)) return new ScalarReader<DateTime>(i => m_Epoch.AddTicks(ReadSecondsAndNanos(i)));
            if (type == typeof(DateTimeOffset)) return new ScalarReader<DateTimeOffset>(i => new DateTimeOffset(m_Epoch.AddTicks(ReadSecondsAndNanos(i))));
            if (type == typeof(TimeSpan)) return new ScalarReader<TimeSpan>(i => new TimeSpan(ReadSecondsAndNanos(i)));
            if (type == typeof(Type)) return new ScalarReader<Type>(i => ProtoTypes.ResolveType(i.ReadString()));

            if (type.FullName == "System.Drawing.Color")
            {
                ParameterExpression input = Expression.Parameter(typeof(ProtoReader));
                Expression argb = Expression.Convert(Expression.Call(input, typeof(ProtoReader).GetMethod(nameof(ProtoReader.ReadFixed32))), typeof(int));
                Expression colour = Expression.Call(type.GetMethod("FromArgb", new[] { typeof(int) }), argb);
                Delegate read = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(ProtoReader), type), colour, input).Compile();
                return New(typeof(ScalarReader<>).MakeGenericType(type), read);
            }

            if (type.IsEnum && ProtoTypes.IsOmType(type))
            {
                ParameterExpression input = Expression.Parameter(typeof(ProtoReader));
                Expression number = Expression.Call(input, typeof(ProtoReader).GetMethod(nameof(ProtoReader.ReadEnum)));
                Delegate read = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(ProtoReader), type), Expression.Convert(number, type), input).Compile();
                return New(typeof(ScalarReader<>).MakeGenericType(type), read);
            }

            if (type.IsEnum)
                return New(typeof(EnumNameReader<>).MakeGenericType(type));

            return null;
        }

        /***************************************************/
        /**** Collections                               ****/
        /***************************************************/

        // Builds a collection of a declared type from the items read.
        private static object CreateBuilder(Type type, Type itemType)
        {
            if (type.FullName == ProtoTypes.FragmentSetName)
                return (Func<List<IFragment>, FragmentSet>)Fragments;

            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
                return Generic(nameof(DictionaryBuilder), type, keyType, valueType);

            if (ProtoTypes.IsTwoDimensionalArray(type))
                return Generic(nameof(MatrixBuilder), type.GetElementType());

            return Generic(nameof(CollectionBuilder), type, itemType);
        }

        /***************************************************/

        private static object Generic(string method, params Type[] types)
        {
            return Generic(method, null, types);
        }

        /***************************************************/

        private static object Generic(string method, object[] arguments, params Type[] types)
        {
            return typeof(ReadPlans).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(types).Invoke(null, arguments);
        }

        /***************************************************/

        // The proto3 default of an absent repeated field: a new empty collection of the declared type.
        private static Func<object> EmptyCollection<C, I>(Func<List<I>, C> builder)
        {
            return () => builder(new List<I>());
        }

        /***************************************************/

        // A FragmentSet holds one fragment per type: a later fragment of the same type replaces the earlier one.
        private static FragmentSet Fragments(List<IFragment> items)
        {
            FragmentSet fragments = new FragmentSet();
            foreach (IFragment fragment in items)
            {
                if (fragment == null)
                    continue;

                if (fragments.Contains(fragment.GetType()))
                {
                    Base.Compute.RecordWarning($"More than one fragment of type {fragment.GetType().FullName} was found. The last one has been kept.");
                    fragments.Remove(fragment.GetType());
                }
                fragments.Add(fragment);
            }
            return fragments;
        }

        /***************************************************/

        private static Func<List<KeyValuePair<K, V>>, D> DictionaryBuilder<D, K, V>()
        {
            Type type = typeof(D);
            Type dictionaryType = typeof(Dictionary<K, V>);
            Type created = type.IsInterface || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null ? dictionaryType : type;
            ConstructorInfo fromDictionary = type.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.IsAssignableFrom(dictionaryType));

            return entries =>
            {
                IDictionary<K, V> dictionary = (IDictionary<K, V>)Activator.CreateInstance(created);
                foreach (KeyValuePair<K, V> entry in entries)
                {
                    if (entry.Key != null)
                        dictionary[entry.Key] = entry.Value;
                }

                if (dictionary is D result)
                    return result;

                // Read-only dictionaries, e.g. ReadOnlyDictionary<TKey, TValue>: construct from a dictionary.
                return fromDictionary == null ? default(D) : (D)fromDictionary.Invoke(new object[] { dictionary });
            };
        }

        /***************************************************/

        private static Func<List<E[]>, E[,]> MatrixBuilder<E>()
        {
            return rows =>
            {
                E[,] matrix = new E[rows.Count, rows.Count == 0 ? 0 : rows.Max(r => r?.Length ?? 0)];
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == null)
                        continue;
                    for (int j = 0; j < rows[i].Length; j++)
                        matrix[i, j] = rows[i][j];
                }
                return matrix;
            };
        }

        /***************************************************/

        private static Func<List<I>, C> CollectionBuilder<C, I>()
        {
            Type type = typeof(C);
            if (type.IsArray)
                return items => (C)(object)items.ToArray();

            if (type.IsAssignableFrom(typeof(List<I>)))
                return items => (C)(object)items;

            // Read-only collections, e.g. ReadOnlyCollection<T>: construct from the list.
            ConstructorInfo fromList = type.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(List<I>)));
            if (fromList != null && type.GetConstructor(Type.EmptyTypes) == null)
                return items => (C)fromList.Invoke(new object[] { items });

            MethodInfo add = type.GetMethod("Add", new[] { typeof(I) });
            return items =>
            {
                object collection = Activator.CreateInstance(type);
                if (collection is ICollection<I> typed)
                {
                    foreach (I item in items)
                        typed.Add(item);
                }
                else if (collection is IList list)
                {
                    foreach (I item in items)
                        list.Add(item);
                }
                else if (add != null)
                {
                    foreach (I item in items)
                        add.Invoke(collection, new object[] { item });
                }
                return (C)collection;
            };
        }

        /***************************************************/
        /**** Setters and defaults                      ****/
        /***************************************************/

        // Sets a property: with its setter, private ones included, else the backing field of a get-only auto property.
        public static Delegate Setter(Type owner, PropertyInfo property)
        {
            Type actionType = typeof(Action<,>).MakeGenericType(owner, property.PropertyType);
            ParameterExpression obj = Expression.Parameter(owner);
            ParameterExpression value = Expression.Parameter(property.PropertyType);

            MethodInfo setter = property.GetSetMethod(true);
            if (setter != null && !owner.IsValueType)
                return Expression.Lambda(actionType, Expression.Call(obj, setter, value), obj, value).Compile();

            FieldInfo backingField = null;
            for (Type t = owner; t != null && backingField == null; t = t.BaseType)
                backingField = t.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

            // Backing fields of get-only auto properties are read-only, which expression trees cannot assign: use reflection.
            Expression set = setter != null ? Expression.Call(Expression.Constant(setter), typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), new[] { typeof(object), typeof(object[]) }),
                    Expression.Convert(obj, typeof(object)), Expression.NewArrayInit(typeof(object), Expression.Convert(value, typeof(object))))
                : backingField != null ? Expression.Call(Expression.Constant(backingField), typeof(FieldInfo).GetMethod(nameof(FieldInfo.SetValue), new[] { typeof(object), typeof(object) }),
                    Expression.Convert(obj, typeof(object)), Expression.Convert(value, typeof(object)))
                : (Expression)Expression.Empty();
            return Expression.Lambda(actionType, set, obj, value).Compile();
        }

        /***************************************************/

        // The proto3 default of an absent single field. Null when the C# default of the property is kept.
        private static Func<object> DefaultValue(Type type)
        {
            if (!ProtocolDefault(type, out object value))
                return null;

            return () => value;
        }

        /***************************************************/

        // The proto3 default of a value absent from the input: null for nullable values, messages, unions and Any, empty
        // for strings, zero for numbers and enums with a value 0. Returns false when the C# default should be kept instead.
        // Not used for repeated values, which default to an empty collection of their declared type.
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

            // Other structs, e.g. System.Drawing.Color: keep the C# default.
            return !type.IsValueType;
        }

        /***************************************************/
        /**** Tuples                                    ****/
        /***************************************************/

        // System.Tuple: a message {1: Item1, 2: Item2, ...}, each item read as a property of its type would be.
        private static object CreateTupleReader(Type type, MessageDescriptor message, ProtoSchema schema)
        {
            Type[] itemTypes = type.GetGenericArguments();
            List<ITupleItemReader> items = new List<ITupleItemReader>();
            for (int i = 0; i < itemTypes.Length; i++)
            {
                Type itemType = itemTypes[i];
                FieldDescriptor field = message.FindFieldByNumber(i + 1);
                if (IsRepeated(itemType))
                {
                    Type innerItemType = ItemType(itemType);
                    object itemReader = CreateItemReader(itemType, field, schema);
                    object builder = CreateBuilder(itemType, innerItemType);
                    items.Add((ITupleItemReader)New(typeof(RepeatedTupleItemReader<,>).MakeGenericType(itemType, innerItemType), itemReader, builder));
                }
                else
                {
                    object defaultValue = ProtocolDefault(itemType, out object value) ? value : null;
                    items.Add((ITupleItemReader)New(typeof(SingleTupleItemReader<>).MakeGenericType(itemType), CreateValueReader(itemType, field, schema), defaultValue));
                }
            }

            return New(typeof(TupleReader<>).MakeGenericType(type), items);
        }

        /***************************************************/
        /**** Well-known types                          ****/
        /***************************************************/

        // The value of a google.protobuf.Any from its type url and content, when it was not read with a plan:
        // well-known types (wrappers, Struct, ListValue), unknown types, or a value written before its type url.
        private static object ReadAnyContent(string typeUrl, byte[] content, ProtoSchema schema)
        {
            string messageName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
            if (messageName == "google.protobuf.Struct")
                return ReadStruct(new ProtoReader(content));

            if (messageName == "google.protobuf.ListValue")
                return ReadListValue(new ProtoReader(content));

            Type wrapped = WrapperType(messageName);
            if (wrapped != null)
                return ReadWrapper(new ProtoReader(content), wrapped);

            // The message may be defined in another installed schema, e.g. of another toolkit.
            IReadPlan plan = AnyPlan(typeUrl, schema);
            if (plan == null)
            {
                Base.Compute.RecordWarning($"The google.protobuf.Any type '{typeUrl}' does not match a known type and has been ignored.");
                return null;
            }

            return plan.ReadObject(new ProtoReader(content));
        }

        /***************************************************/

        private static Type WrapperType(string messageName)
        {
            switch (messageName)
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

        // google.protobuf.DoubleValue, StringValue, ... { value = 1; }: the value of the first field, or the proto3 default.
        private static object ReadWrapper(ProtoReader input, Type type)
        {
            if (input.ReadTag() == 0)
                return type == typeof(string) ? "" : Activator.CreateInstance(type);

            if (type == typeof(double)) return input.ReadDouble();
            if (type == typeof(float)) return input.ReadFloat();
            if (type == typeof(int)) return input.ReadInt32();
            if (type == typeof(long)) return input.ReadInt64();
            if (type == typeof(uint)) return input.ReadUInt32();
            if (type == typeof(ulong)) return input.ReadUInt64();
            if (type == typeof(bool)) return input.ReadBool();
            return input.ReadString();
        }

        /***************************************************/

        // google.protobuf.Struct { map<string, Value> fields = 1; }, up to the end of the input or its current limit.
        private static Dictionary<string, object> ReadStruct(ProtoReader input)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
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
                int limit = input.PushLimit(input.ReadLength());
                uint entryTag;
                while ((entryTag = input.ReadTag()) != 0)
                {
                    int number = WireFormat.GetTagFieldNumber(entryTag);
                    if (number == 1)
                    {
                        key = input.ReadString();
                    }
                    else if (number == 2)
                    {
                        int valueLimit = input.PushLimit(input.ReadLength());
                        value = ReadStructValue(input);
                        input.PopLimit(valueLimit);
                    }
                    else
                    {
                        input.SkipLastField();
                    }
                }
                input.PopLimit(limit);
                result[key] = value;
            }
            return result;
        }

        /***************************************************/

        // google.protobuf.Value { oneof kind { NullValue null_value = 1; double number_value = 2; string string_value = 3;
        // bool bool_value = 4; Struct struct_value = 5; ListValue list_value = 6; } }
        private static object ReadStructValue(ProtoReader input)
        {
            object result = null;
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (WireFormat.GetTagFieldNumber(tag))
                {
                    case 1:
                        input.ReadEnum();
                        result = null;
                        break;
                    case 2:
                        result = input.ReadDouble();
                        break;
                    case 3:
                        result = input.ReadString();
                        break;
                    case 4:
                        result = input.ReadBool();
                        break;
                    case 5:
                        int structLimit = input.PushLimit(input.ReadLength());
                        result = ReadStruct(input);
                        input.PopLimit(structLimit);
                        break;
                    case 6:
                        int listLimit = input.PushLimit(input.ReadLength());
                        result = ReadListValue(input);
                        input.PopLimit(listLimit);
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
            return result;
        }

        /***************************************************/

        // google.protobuf.ListValue { repeated Value values = 1; }
        private static List<object> ReadListValue(ProtoReader input)
        {
            List<object> list = new List<object>();
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) != 1)
                {
                    input.SkipLastField();
                    continue;
                }

                int limit = input.PushLimit(input.ReadLength());
                list.Add(ReadStructValue(input));
                input.PopLimit(limit);
            }
            return list;
        }

        /***************************************************/

        // google.protobuf.Timestamp and Duration { int64 seconds = 1; int32 nanos = 2; }, as ticks.
        private static long ReadSecondsAndNanos(ProtoReader input)
        {
            long seconds = 0;
            int nanos = 0;
            int limit = input.PushLimit(input.ReadLength());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (WireFormat.GetTagFieldNumber(tag))
                {
                    case 1:
                        seconds = input.ReadInt64();
                        break;
                    case 2:
                        nanos = input.ReadInt32();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
            input.PopLimit(limit);
            return seconds * TimeSpan.TicksPerSecond + nanos / 100;
        }

        /***************************************************/
        /**** Helpers                                   ****/
        /***************************************************/

        private static object New(Type type, params object[] arguments)
        {
            return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private static readonly DateTime m_Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly ConcurrentDictionary<(ProtoSchema, Type, MessageDescriptor), IReadPlan> m_Plans = new ConcurrentDictionary<(ProtoSchema, Type, MessageDescriptor), IReadPlan>();

        private static readonly Dictionary<(ProtoSchema, Type, MessageDescriptor), IReadPlan> m_Pending = new Dictionary<(ProtoSchema, Type, MessageDescriptor), IReadPlan>();

        private static readonly ConcurrentDictionary<(ProtoSchema, string), IReadPlan> m_AnyPlans = new ConcurrentDictionary<(ProtoSchema, string), IReadPlan>();

        private static readonly object m_Lock = new object();

        private static int m_Depth = 0;

        /***************************************************/
    }
}
