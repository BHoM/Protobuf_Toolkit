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
    [Description("A property of T, read with a compiled getter and written with the codec of its declared type.")]
    public sealed class FieldWriter<T, V> : IFieldWriter<T>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public FieldWriter(Func<T, V> getter, Codec<V> codec)
        {
            m_Getter = getter;
            m_Codec = codec;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public int Size(T obj, WriteContext context) => m_Codec.Size(m_Getter(obj), context);

        /***************************************************/

        public void Write(T obj, WriteContext context) => m_Codec.Write(m_Getter(obj), context);

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly Func<T, V> m_Getter;

        private readonly Codec<V> m_Codec;

        /***************************************************/
    }
}
