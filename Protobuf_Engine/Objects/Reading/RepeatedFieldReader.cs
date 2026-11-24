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
    [Description("Reads a repeated field of a property, e.g. a collection or a dictionary: collects the occurrences, then builds the declared collection type.")]
    public sealed class RepeatedFieldReader<T, C, I> : FieldReader<T>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public RepeatedFieldReader(int index, ItemReader<I> items, Func<List<I>, C> builder, Action<T, C> setter, Func<object> defaultValue) : base(index)
        {
            m_Items = items;
            m_Builder = builder;
            m_Setter = setter;
            m_Default = defaultValue;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override void Read(T obj, ReadState state, ProtoReader input, WireFormat.WireType wireType)
        {
            if (state.Lists == null)
                state.Lists = new object[state.Seen.Length];

            List<I> items = (List<I>)state.Lists[Index];
            if (items == null)
                state.Lists[Index] = items = new List<I>();

            m_Items.ReadInto(items, input, wireType);
        }

        /***************************************************/

        public override void Finish(T obj, ReadState state, bool defaults)
        {
            if (!defaults)
            {
                List<I> items = state.Lists?[Index] as List<I>;
                if (items == null)
                    return;

                Set(obj, state, m_Builder(items));
                return;
            }

            if (state.Seen[Index] || m_Default == null)
                return;

            Set(obj, state, (C)m_Default());
        }

        /***************************************************/

        public override void SetBoxed(T obj, object value) => m_Setter(obj, (C)value);

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private void Set(T obj, ReadState state, C value)
        {
            state.Seen[Index] = true;
            if (state.Values != null)
                state.Values[Index] = value;
            else
                m_Setter(obj, value);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ItemReader<I> m_Items;

        private readonly Func<List<I>, C> m_Builder;

        private readonly Action<T, C> m_Setter;

        private readonly Func<object> m_Default;

        /***************************************************/
    }
}
