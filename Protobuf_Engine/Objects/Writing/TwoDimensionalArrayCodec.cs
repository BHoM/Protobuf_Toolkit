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
    [Description("A two dimensional array: each row wrapped in a message { repeated items = 1; }. Rare: rows are copied out.")]
    public sealed class TwoDimensionalArrayCodec<V> : TaggedCodec<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public TwoDimensionalArrayCodec(int number, Type elementType, ICodec row) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_ElementType = elementType;
            m_Row = row;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(V value, WriteContext context)
        {
            if (value == null)
                return 0;

            int size = 0;
            foreach (Array row in Rows((Array)(object)value))
            {
                int index = context.Reserve();
                int inner = m_Row.SizeObject(row, context);
                context.Sizes[index] = inner;
                size += TagSize + CodedOutputStream.ComputeLengthSize(inner) + inner;
            }
            return size;
        }

        /***************************************************/

        public override void Write(V value, WriteContext context)
        {
            if (value == null)
                return;

            foreach (Array row in Rows((Array)(object)value))
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteLength(context.Sizes[context.NextSize++]);
                m_Row.WriteObject(row, context);
            }
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private IEnumerable<Array> Rows(Array array)
        {
            for (int i = 0; i < array.GetLength(0); i++)
            {
                Array row = Array.CreateInstance(m_ElementType, array.GetLength(1));
                for (int j = 0; j < array.GetLength(1); j++)
                    row.SetValue(array.GetValue(i, j), j);
                yield return row;
            }
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly Type m_ElementType;

        private readonly ICodec m_Row;

        /***************************************************/
    }
}
