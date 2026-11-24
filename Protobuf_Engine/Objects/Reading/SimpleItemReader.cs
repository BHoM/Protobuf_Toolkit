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
    [Description("Reads the items of a repeated field one by one, or packed for numeric types.")]
    public sealed class SimpleItemReader<I> : ItemReader<I>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public SimpleItemReader(ValueReader<I> reader, bool packable)
        {
            m_Reader = reader;
            m_Packable = packable;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override void ReadInto(List<I> items, ProtoReader input, WireFormat.WireType wireType)
        {
            if (m_Packable && wireType == WireFormat.WireType.LengthDelimited)
            {
                // Packed repeated scalars: the default encoding of other protobuf writers.
                int limit = input.PushLimit(input.ReadLength());
                while (!input.IsAtEnd)
                    items.Add(m_Reader.Read(input, wireType));
                input.PopLimit(limit);
                return;
            }

            items.Add(m_Reader.Read(input, wireType));
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ValueReader<I> m_Reader;

        private readonly bool m_Packable;

        /***************************************************/
    }
}
