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
using System.ComponentModel;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("A value written as another type, e.g. a short as an int or an oM enum as its number.")]
    public sealed class ConvertCodec<V, U> : Codec<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public ConvertCodec(Func<V, U> convert, Codec<U> inner)
        {
            m_Convert = convert;
            m_Inner = inner;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(V value, WriteContext context) => value == null ? 0 : m_Inner.Size(m_Convert(value), context);

        /***************************************************/

        public override void Write(V value, WriteContext context)
        {
            if (value != null)
                m_Inner.Write(m_Convert(value), context);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly Func<V, U> m_Convert;

        private readonly Codec<U> m_Inner;

        /***************************************************/
    }
}
