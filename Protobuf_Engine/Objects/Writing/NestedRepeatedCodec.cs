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
using BH.oM.Base;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("A collection of collections: each inner collection wrapped in a message { repeated items = 1; }.")]
    public sealed class NestedRepeatedCodec<C, I> : TaggedCodec<C>
        where C : IEnumerable<I>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public NestedRepeatedCodec(int number, Codec<I> items) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_Items = items;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(C value, WriteContext context)
        {
            if (value == null)
                return 0;

            int size = 0;
            foreach (I item in value)
            {
                if (item == null)
                    continue;

                int index = context.Reserve();
                int inner = m_Items.Size(item, context);
                context.Sizes[index] = inner;
                size += TagSize + CodedOutputStream.ComputeLengthSize(inner) + inner;
            }
            return size;
        }

        /***************************************************/

        public override void Write(C value, WriteContext context)
        {
            if (value == null)
                return;

            foreach (I item in value)
            {
                if (item == null)
                    continue;

                context.Output.WriteTag(Tag);
                context.Output.WriteLength(context.Sizes[context.NextSize++]);
                m_Items.Write(item, context);
            }
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly Codec<I> m_Items;

        /***************************************************/
    }
}
