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
    [Description("A collection: each item written in the same field, null items left out.")]
    public sealed class RepeatedCodec<C, I> : Codec<C>
        where C : IEnumerable<I>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public RepeatedCodec(Codec<I> item)
        {
            m_Item = item;
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
                if (item != null)
                    size += m_Item.Size(item, context);
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
                if (item != null)
                    m_Item.Write(item, context);
            }
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly Codec<I> m_Item;

        /***************************************************/
    }
}
