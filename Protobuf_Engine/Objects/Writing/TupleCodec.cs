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
using System.Reflection;
using BH.oM.Base;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Writes a System.Tuple as a message {1: Item1, 2: Item2, ...}. Rare: the items are read with reflection.")]
    public sealed class TupleCodec<V> : TaggedCodec<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public TupleCodec(int number, List<ICodec> items) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_Items = items;
            m_Properties = Enumerable.Range(1, items.Count).Select(i => typeof(V).GetProperty($"Item{i}")).ToList();
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(V value, WriteContext context)
        {
            if (value == null)
                return 0;

            int index = context.Reserve();
            int inner = 0;
            for (int i = 0; i < m_Items.Count; i++)
                inner += m_Items[i].SizeObject(m_Properties[i].GetValue(value), context);
            context.Sizes[index] = inner;
            return TagSize + CodedOutputStream.ComputeLengthSize(inner) + inner;
        }

        /***************************************************/

        public override void Write(V value, WriteContext context)
        {
            if (value == null)
                return;

            context.Output.WriteTag(Tag);
            context.Output.WriteLength(context.Sizes[context.NextSize++]);
            for (int i = 0; i < m_Items.Count; i++)
                m_Items[i].WriteObject(m_Properties[i].GetValue(value), context);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly List<ICodec> m_Items;

        private readonly List<PropertyInfo> m_Properties;

        /***************************************************/
    }
}
