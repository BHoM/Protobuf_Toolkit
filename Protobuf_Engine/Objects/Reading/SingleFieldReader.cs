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
using Google.Protobuf;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Reads a single valued field of a property: the last occurrence wins, and an absent field gets the proto3 default.")]
    public sealed class SingleFieldReader<T, V> : FieldReader<T>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public SingleFieldReader(int index, ValueReader<V> reader, Action<T, V> setter, Func<object> defaultValue) : base(index)
        {
            m_Reader = reader;
            m_Setter = setter;
            m_Default = defaultValue;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override void Read(T obj, ReadState state, ProtoReader input, WireFormat.WireType wireType)
        {
            V value = m_Reader.Read(input, wireType);
            state.Seen[Index] = true;
            if (state.Values != null)
                state.Values[Index] = value;
            else
                m_Setter(obj, value);
        }

        /***************************************************/

        public override void Finish(T obj, ReadState state, bool defaults)
        {
            if (!defaults || state.Seen[Index] || m_Default == null)
                return;

            object value = m_Default();
            state.Seen[Index] = true;
            if (state.Values != null)
                state.Values[Index] = value;
            else
                m_Setter(obj, (V)value);
        }

        /***************************************************/

        public override void SetBoxed(T obj, object value) => m_Setter(obj, (V)value);

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ValueReader<V> m_Reader;

        private readonly Action<T, V> m_Setter;

        private readonly Func<object> m_Default;

        /***************************************************/
    }
}
