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
using System.Collections.Concurrent;
using System.ComponentModel;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Reads a union generated from an interface or abstract class: the message of the slot read, or the value of its other google.protobuf.Any slot.")]
    public sealed class UnionReader<V> : ValueReader<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public UnionReader(MessageDescriptor union, ProtoSchema schema)
        {
            m_Union = union;
            m_Schema = schema;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override V Read(ProtoReader input, WireFormat.WireType wireType)
        {
            object result = null;
            int limit = input.PushLimit(input.ReadLength());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int number = WireFormat.GetTagFieldNumber(tag);
                Slot slot = SlotOf(number);
                if (slot.Kind == SlotKind.Undefined)
                {
                    Base.Compute.RecordWarning($"Field {number} of the union {m_Union.FullName} is not defined and has been ignored.");
                    input.SkipLastField();
                    continue;
                }

                if (slot.Kind == SlotKind.UnknownType)
                {
                    Base.Compute.RecordWarning($"The C# type of {slot.MessageName} could not be found, and the value has been ignored.");
                    input.SkipLastField();
                    continue;
                }

                int inner = input.PushLimit(input.ReadLength());
                result = slot.Kind == SlotKind.Any ? ReadPlans.ReadAny(input, m_Schema, typeof(V)) : slot.Plan.ReadObject(input);
                input.PopLimit(inner);
            }
            input.PopLimit(limit);

            if (result == null || result is V)
                return (V)result;

            Base.Compute.RecordWarning($"Could not read a {result.GetType().Name} as a {typeof(V).Name}.");
            return default(V);
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private Slot SlotOf(int number)
        {
            return m_Slots.GetOrAdd(number, n =>
            {
                FieldDescriptor field = m_Union.FindFieldByNumber(n);
                if (field == null || field.FieldType != FieldType.Message)
                    return new Slot
                    {
                        Kind = SlotKind.Undefined
                    };
                if (ProtoSchema.IsAny(field.MessageType))
                    return new Slot
                    {
                        Kind = SlotKind.Any
                    };

                Type concrete = m_Schema.TypeOf(field.MessageType);
                if (concrete == null)
                    return new Slot
                    {
                        Kind = SlotKind.UnknownType,
                        MessageName = field.MessageType.FullName
                    };

                return new Slot
                {
                    Kind = SlotKind.Message,
                    Plan = ReadPlans.Get(m_Schema, concrete, field.MessageType)
                };
            });
        }

        /***************************************************/
        /**** Private Types                             ****/
        /***************************************************/

        private enum SlotKind { Undefined, Any, UnknownType, Message }

        /***************************************************/

        private sealed class Slot
        {
            public SlotKind Kind;
            public IReadPlan Plan;
            public string MessageName;
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly MessageDescriptor m_Union;

        private readonly ProtoSchema m_Schema;

        private readonly ConcurrentDictionary<int, Slot> m_Slots = new ConcurrentDictionary<int, Slot>();

        /***************************************************/
    }
}
