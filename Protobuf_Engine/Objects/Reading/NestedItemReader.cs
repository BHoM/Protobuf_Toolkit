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
    [Description("A collection of collections: each inner collection wrapped in a message { repeated items = 1; }.")]
    public sealed class NestedItemReader<I, J> : ItemReader<I>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public NestedItemReader(ItemReader<J> inner, Func<List<J>, I> builder)
        {
            m_Inner = inner;
            m_Builder = builder;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override void ReadInto(List<I> items, ProtoReader input, WireFormat.WireType wireType)
        {
            List<J> inner = new List<J>();
            int limit = input.PushLimit(input.ReadLength());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) == 1)
                    m_Inner.ReadInto(inner, input, WireFormat.GetTagWireType(tag));
                else
                    input.SkipLastField();
            }
            input.PopLimit(limit);
            items.Add(m_Builder(inner));
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ItemReader<J> m_Inner;

        private readonly Func<List<J>, I> m_Builder;

        /***************************************************/
    }
}
