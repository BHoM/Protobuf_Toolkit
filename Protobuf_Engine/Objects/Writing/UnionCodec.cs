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
    [Description("A union generated from an interface or abstract class: the slot of the value's C# type, else the 'other' Any slot.")]
    public sealed class UnionCodec<V> : TaggedCodec<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public UnionCodec(int number, MessageDescriptor union, ProtoSchema schema) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_Union = union;
            m_Schema = schema;
            FieldDescriptor other = union.FindFieldByNumber(ProtoTypes.OtherFieldNumber);
            if (other != null)
                m_Other = new AnyCodec<object>(other.FieldNumber, schema);
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(V value, WriteContext context)
        {
            if (value == null)
                return 0;

            Slot slot = SlotOf(value.GetType());
            int content;
            if (slot.Plan != null)
            {
                int outer = context.Reserve();
                int inner = context.Reserve();
                int message = slot.Plan.SizeObject(value, context);
                context.Sizes[inner] = message;
                content = slot.TagSize + CodedOutputStream.ComputeLengthSize(message) + message;
                context.Sizes[outer] = content;
            }
            else if (m_Other != null)
            {
                // The union's length goes before the Any's own entries: insert it once the Any is sized.
                int position = context.Sizes.Count;
                int any = m_Other.Size(value, context);
                if (any == 0)
                    return 0; // The value cannot be written as an Any: the union is left out.
                content = any;
                context.Sizes.Insert(position, content);
            }
            else
            {
                // Closed unions, e.g. IGeometry, only cover the implementations of their own file.
                Base.Compute.RecordWarning($"{value.GetType().FullName} has no slot in the union {m_Union.FullName} and has been left out.");
                return 0;
            }

            return TagSize + CodedOutputStream.ComputeLengthSize(content) + content;
        }

        /***************************************************/

        public override void Write(V value, WriteContext context)
        {
            if (value == null)
                return;

            Slot slot = SlotOf(value.GetType());
            if (slot.Plan != null)
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteLength(context.Sizes[context.NextSize++]);
                context.Output.WriteTag(slot.Tag);
                context.Output.WriteLength(context.Sizes[context.NextSize++]);
                slot.Plan.WriteObject(value, context);
            }
            else if (m_Other != null && !m_Other.SkipIfUnwritable(value, context))
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteLength(context.Sizes[context.NextSize++]);
                m_Other.Write(value, context);
            }
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private Slot SlotOf(Type type)
        {
            Slot last = m_Last;
            if (last != null && last.Type == type)
                return last;

            Slot slot = m_Slots.GetOrAdd(type, t =>
            {
                FieldDescriptor field = m_Schema.UnionSlot(m_Union, t.FullName);
                if (field == null)
                    return new Slot
                    {
                        Type = t
                    };

                uint tag = WritePlans.Tag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                return new Slot
                {
                    Type = t,
                    Plan = WritePlans.Get(m_Schema, t, field.MessageType),
                    Tag = tag,
                    TagSize = WritePlans.TagSize(tag)
                };
            });

            m_Last = slot;
            return slot;
        }

        /***************************************************/
        /**** Private Types                             ****/
        /***************************************************/

        private sealed class Slot
        {
            public Type Type;
            public IMessagePlan Plan;
            public uint Tag;
            public int TagSize;
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly MessageDescriptor m_Union;

        private readonly ProtoSchema m_Schema;

        private readonly AnyCodec<object> m_Other;

        private readonly ConcurrentDictionary<Type, Slot> m_Slots = new ConcurrentDictionary<Type, Slot>();

        // The last slot, which knows its type, skips the dictionary.
        private volatile Slot m_Last;

        /***************************************************/
    }
}
