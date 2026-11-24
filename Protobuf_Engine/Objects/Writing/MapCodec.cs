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
    [Description("A dictionary: each entry a message {1: key, 2: value}, for both map<,> fields and key/value wrapper messages.")]
    public sealed class MapCodec<D, K, V> : TaggedCodec<D>
        where D : IEnumerable<KeyValuePair<K, V>>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public MapCodec(int number, Codec<K> key, Codec<V> value) : base(number, WireFormat.WireType.LengthDelimited)
        {
            m_Key = key;
            m_Value = value;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(D value, WriteContext context)
        {
            if (value == null)
                return 0;

            int size = 0;
            foreach (KeyValuePair<K, V> entry in value)
            {
                int index = context.Reserve();
                int inner = m_Key.Size(entry.Key, context) + m_Value.Size(entry.Value, context);
                context.Sizes[index] = inner;
                size += TagSize + CodedOutputStream.ComputeLengthSize(inner) + inner;
            }
            return size;
        }

        /***************************************************/

        public override void Write(D value, WriteContext context)
        {
            if (value == null)
                return;

            foreach (KeyValuePair<K, V> entry in value)
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteLength(context.Sizes[context.NextSize++]);
                m_Key.Write(entry.Key, context);
                m_Value.Write(entry.Value, context);
            }
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly Codec<K> m_Key;

        private readonly Codec<V> m_Value;

        /***************************************************/
    }
}
