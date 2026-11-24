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
    [Description("Reads an item of a System.Tuple that holds a collection: the occurrences are collected and built into the declared collection type, empty when absent.")]
    public sealed class RepeatedTupleItemReader<C, I> : ITupleItemReader
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public RepeatedTupleItemReader(ItemReader<I> items, Func<List<I>, C> builder)
        {
            m_Items = items;
            m_Builder = builder;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public void Read(object[] values, int index, ProtoReader input, WireFormat.WireType wireType)
        {
            List<I> items = values[index] as List<I>;
            if (items == null)
                values[index] = items = new List<I>();

            m_Items.ReadInto(items, input, wireType);
        }

        /***************************************************/

        public object Finish(object value)
        {
            return m_Builder(value as List<I> ?? new List<I>());
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ItemReader<I> m_Items;

        private readonly Func<List<I>, C> m_Builder;

        /***************************************************/
    }
}
