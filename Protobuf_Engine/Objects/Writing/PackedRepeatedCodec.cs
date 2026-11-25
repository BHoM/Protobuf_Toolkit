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
using System.Collections.Generic;
using System.ComponentModel;
using Google.Protobuf;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("A collection of numbers, bools or enums in a packed field: one tag and length, then the values without their tags. Empty collections are left out.")]
    public sealed class PackedRepeatedCodec<C, I> : TaggedCodec<C>
        where C : IEnumerable<I>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public PackedRepeatedCodec(int number, IPackableCodec<I> item) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_Item = item;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(C value, WriteContext context)
        {
            int content = ContentSize(value);
            return content == 0 ? 0 : TagSize + CodedOutputStream.ComputeLengthSize(content) + content;
        }

        /***************************************************/

        public override void Write(C value, WriteContext context)
        {
            int content = ContentSize(value);
            if (content == 0)
                return;

            context.Output.WriteTag(Tag);
            context.Output.WriteLength(content);
            foreach (I item in value)
            {
                if (item != null)
                    m_Item.WriteValue(item, context);
            }
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        // Every value takes at least one byte, so the content is only empty when there is no value.
        private int ContentSize(C value)
        {
            if (value == null)
                return 0;

            int size = 0;
            foreach (I item in value)
            {
                if (item != null)
                    size += m_Item.ValueSize(item);
            }
            return size;
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly IPackableCodec<I> m_Item;

        /***************************************************/
    }
}
