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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Compiled plan reading the protobuf message of a C# type: field readers by field number, compiled creation or constructor, and the proto3 defaults of absent fields.")]
    public sealed class ReadPlan<T> : IReadPlan
    {
        public ReadPlan(ProtoSchema schema, MessageDescriptor message)
        {
            m_Schema = schema;
            m_Message = message;
        }

        public void Initialise()
        {
            Type type = typeof(T);
            Dictionary<string, PropertyInfo> properties = ProtoTypes.PropertiesByName(type);
            List<(FieldDescriptor Field, PropertyInfo Property)> fields = m_Schema.PropertyFields(m_Message)
                .Where(kvp => properties.ContainsKey(kvp.Key))
                .Select(kvp => (kvp.Value, properties[kvp.Key]))
                .ToList();

            m_Fields = fields.Select((f, i) => (FieldReader<T>)ReadPlans.CreateFieldReader(type, f.Property, f.Field, i, m_Schema)).ToArray();
            m_Properties = fields.Select(f => f.Property).ToArray();

            int max = fields.Count == 0 ? 0 : fields.Max(f => f.Field.FieldNumber);
            m_ByNumber = new FieldReader<T>[Math.Min(max, 255) + 1];
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Field.FieldNumber < m_ByNumber.Length)
                    m_ByNumber[fields[i].Field.FieldNumber] = m_Fields[i];
                else
                    m_Large[fields[i].Field.FieldNumber] = m_Fields[i];
            }

            // Created up front with a public parameterless constructor, else built from the values with a constructor.
            if (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) != null)
                m_Create = Expression.Lambda<Func<T>>(Expression.New(type)).Compile();
            else
                PrepareConstructor(type);
        }

        public object ReadObject(ProtoReader input)
        {
            bool buffered = m_Create == null;
            ReadState state = new ReadState(m_Fields.Length, buffered);
            T obj = buffered ? default(T) : m_Create();

            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int number = WireFormat.GetTagFieldNumber(tag);
                FieldReader<T> field = number < m_ByNumber.Length ? m_ByNumber[number] : (m_Large.TryGetValue(number, out FieldReader<T> large) ? large : null);
                if (field == null)
                {
                    input.SkipLastField(); // Unknown field, e.g. written with a newer version of the definitions.
                    continue;
                }

                field.Read(obj, state, input, WireFormat.GetTagWireType(tag));
            }

            // Repeated fields, then the proto3 defaults of the absent fields.
            foreach (FieldReader<T> field in m_Fields)
                field.Finish(obj, state, false);
            foreach (FieldReader<T> field in m_Fields)
                field.Finish(obj, state, true);

            return buffered ? Construct(state) : obj;
        }

        /***************************************************/

        // The constructor with the most parameters that all match properties, by name.
        private void PrepareConstructor(Type type)
        {
            m_Constructor = type.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault(c => c.GetParameters().All(p => m_Properties.Any(v => string.Equals(v.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                    || type.GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null));

            if (m_Constructor == null)
                return;

            ParameterInfo[] parameters = m_Constructor.GetParameters();
            m_ParameterSlots = parameters.Select(p => Array.FindIndex(m_Properties, v => string.Equals(v.Name, p.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
            m_ParameterDefaults = parameters.Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        }

        private object Construct(ReadState state)
        {
            bool[] used = new bool[m_Fields.Length];
            object result;
            if (m_Constructor != null)
            {
                object[] arguments = new object[m_ParameterSlots.Length];
                for (int i = 0; i < arguments.Length; i++)
                {
                    int slot = m_ParameterSlots[i];
                    if (slot >= 0 && state.Seen[slot])
                    {
                        arguments[i] = state.Values[slot];
                        used[slot] = true;
                    }
                    else
                    {
                        arguments[i] = m_ParameterDefaults[i];
                    }
                }
                result = m_Constructor.Invoke(arguments);
            }
            else
            {
                result = FormatterServices.GetUninitializedObject(typeof(T));
            }

            T obj = (T)result;
            for (int i = 0; i < m_Fields.Length; i++)
            {
                if (state.Seen[i] && !used[i])
                    m_Fields[i].SetBoxed(obj, state.Values[i]);
            }
            return obj;
        }

        /***************************************************/

        private readonly ProtoSchema m_Schema;

        private readonly MessageDescriptor m_Message;

        private FieldReader<T>[] m_Fields = new FieldReader<T>[0];

        private PropertyInfo[] m_Properties = new PropertyInfo[0];

        private FieldReader<T>[] m_ByNumber = new FieldReader<T>[0];

        private readonly Dictionary<int, FieldReader<T>> m_Large = new Dictionary<int, FieldReader<T>>();

        private Func<T> m_Create;

        private ConstructorInfo m_Constructor;

        private int[] m_ParameterSlots;

        private object[] m_ParameterDefaults;
    }
}
