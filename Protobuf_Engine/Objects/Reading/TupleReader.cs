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
using Google.Protobuf;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Reads a System.Tuple from a message {1: Item1, 2: Item2, ...}, each item read as a property of its type would be, then built with the constructor of the tuple.")]
    public sealed class TupleReader<V> : ValueReader<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public TupleReader(List<ITupleItemReader> items)
        {
            m_Items = items;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override V Read(ProtoReader input, WireFormat.WireType wireType)
        {
            object[] values = new object[m_Items.Count];
            int limit = input.PushLimit(input.ReadLength());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int index = WireFormat.GetTagFieldNumber(tag) - 1;
                if (index < 0 || index >= m_Items.Count)
                {
                    input.SkipLastField();
                    continue;
                }

                m_Items[index].Read(values, index, input, WireFormat.GetTagWireType(tag));
            }
            input.PopLimit(limit);

            for (int i = 0; i < m_Items.Count; i++)
                values[i] = m_Items[i].Finish(values[i]);

            return (V)Activator.CreateInstance(typeof(V), values);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly List<ITupleItemReader> m_Items;

        /***************************************************/
    }
}
