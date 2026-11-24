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
using BH.oM.Base;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Writes a google.protobuf.Any: a message when the C# type of the value has one, else a well-known type (wrapper, Struct, ListValue), encoded once in the size pass.")]
    public sealed class AnyCodec<V> : TaggedCodec<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public AnyCodec(int number, ProtoSchema schema) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_Schema = schema;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(V value, WriteContext context)
        {
            if (value == null)
                return 0;

            int content = SizeContent(value, context);
            return content < 0 ? 0 : TagSize + CodedOutputStream.ComputeLengthSize(content) + content;
        }

        /***************************************************/

        public override void Write(V value, WriteContext context)
        {
            if (value == null || SkipIfUnwritable(value, context))
                return;

            context.Output.WriteTag(Tag);
            WriteContent(value, context);
        }

        /***************************************************/

        // Size of the Any message itself, or -1 when the value cannot be written.
        public int SizeContent(object value, WriteContext context)
        {
            Target target = TargetOf(value.GetType());
            if (target.Plan == null)
            {
                byte[] blob = WritePlans.EncodeWellKnownAny(value);
                context.Blobs.Add(blob);
                return blob == null ? -1 : blob.Length;
            }

            int index = context.Reserve();
            int inner = target.Plan.SizeObject(value, context);
            context.Sizes[index] = inner;
            return WritePlans.AnyEnvelopeSize(target.Url, inner);
        }

        /***************************************************/

        // In the write pass: true, with its pre-encoded entry consumed, when the size pass found the value cannot be written.
        public bool SkipIfUnwritable(object value, WriteContext context)
        {
            if (TargetOf(value.GetType()).Plan != null || context.Blobs[context.NextBlob] != null)
                return false;

            context.NextBlob++;
            return true;
        }

        /***************************************************/

        // Writes the length and content of the Any message.
        public void WriteContent(object value, WriteContext context)
        {
            Target target = TargetOf(value.GetType());
            if (target.Plan == null)
            {
                context.Output.WriteBytes(UnsafeByteOperations.UnsafeWrap(context.Blobs[context.NextBlob++]));
                return;
            }

            int inner = context.Sizes[context.NextSize++];
            context.Output.WriteLength(WritePlans.AnyEnvelopeSize(target.Url, inner));
            WritePlans.WriteAnyEnvelope(target.Url, inner, context);
            target.Plan.WriteObject(value, context);
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        // The message of a C# type, in this schema or in a registered one. Taken once per type.
        private Target TargetOf(Type type)
        {
            Target last = m_Last;
            if (last != null && last.Type == type)
                return last;

            Target target = m_Targets.GetOrAdd(type, t =>
            {
                ProtoSchema owner = m_Schema.MessageOf(t) != null ? m_Schema
                    : typeof(IObject).IsAssignableFrom(t) ? ProtoSchema.Find(t, "", true)
                    : null;
                if (owner == null)
                    return new Target
                    {
                        Type = t
                    };

                MessageDescriptor message = owner.MessageOf(t);
                return new Target
                {
                    Type = t,
                    Plan = WritePlans.Get(owner, t, message),
                    Url = ProtoTypes.TypeUrlPrefix + message.FullName
                };
            });

            m_Last = target;
            return target;
        }

        /***************************************************/
        /**** Private Types                             ****/
        /***************************************************/

        private sealed class Target
        {
            public Type Type;
            public IMessagePlan Plan;
            public string Url;
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ProtoSchema m_Schema;

        private readonly ConcurrentDictionary<Type, Target> m_Targets = new ConcurrentDictionary<Type, Target>();

        // Values of a field are often all of one type: the last target, which knows its type, skips the dictionary.
        private volatile Target m_Last;

        /***************************************************/
    }
}
