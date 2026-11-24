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
using System.Reflection;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Compiled plan writing the protobuf message of a C# type: one field writer per property with a field, in field number order.")]
    public sealed class MessagePlan<T> : IMessagePlan
    {
        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        public MessageDescriptor Message { get; }

        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public MessagePlan(ProtoSchema schema, MessageDescriptor message)
        {
            m_Schema = schema;
            Message = message;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        // One writer per property with a field, in field number order.
        public void Initialise()
        {
            Dictionary<string, PropertyInfo> properties = ProtoTypes.PropertiesByName(typeof(T));
            List<IFieldWriter<T>> fields = new List<IFieldWriter<T>>();
            foreach (KeyValuePair<string, FieldDescriptor> kvp in m_Schema.PropertyFields(Message))
            {
                if (!properties.TryGetValue(kvp.Key, out PropertyInfo property))
                    continue;

                ICodec codec = WritePlans.CreateCodec(property.PropertyType, kvp.Value, false, m_Schema);
                Type writerType = typeof(FieldWriter<,>).MakeGenericType(typeof(T), property.PropertyType);
                fields.Add((IFieldWriter<T>)Activator.CreateInstance(writerType, WritePlans.Getter(typeof(T), property), codec));
            }
            m_Fields = fields.ToArray();
        }

        /***************************************************/

        public int Size(T obj, WriteContext context)
        {
            int size = 0;
            foreach (IFieldWriter<T> field in m_Fields)
                size += field.Size(obj, context);
            return size;
        }

        /***************************************************/

        public void Write(T obj, WriteContext context)
        {
            foreach (IFieldWriter<T> field in m_Fields)
                field.Write(obj, context);
        }

        /***************************************************/

        public int SizeObject(object obj, WriteContext context) => Size((T)obj, context);

        /***************************************************/

        public void WriteObject(object obj, WriteContext context) => Write((T)obj, context);

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ProtoSchema m_Schema;

        private IFieldWriter<T>[] m_Fields = new IFieldWriter<T>[0];

        /***************************************************/
    }
}
