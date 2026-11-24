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
    [Description("An entry of a dictionary whose values are collections: a message {1: key, repeated 2: items}.")]
    public sealed class RepeatedMapEntryReader<K, V, J> : ItemReader<KeyValuePair<K, V>>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public RepeatedMapEntryReader(ValueReader<K> key, object keyDefault, ItemReader<J> items, Func<List<J>, V> builder)
        {
            m_Key = key;
            m_KeyDefault = keyDefault is K k ? k : default(K);
            m_Items = items;
            m_Builder = builder;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override void ReadInto(List<KeyValuePair<K, V>> items, ProtoReader input, WireFormat.WireType wireType)
        {
            K key = m_KeyDefault;
            List<J> values = null;

            int limit = input.PushLimit(input.ReadLength());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int number = WireFormat.GetTagFieldNumber(tag);
                if (number == 1)
                    key = m_Key.Read(input, WireFormat.GetTagWireType(tag));
                else if (number == 2)
                    m_Items.ReadInto(values = values ?? new List<J>(), input, WireFormat.GetTagWireType(tag));
                else
                    input.SkipLastField();
            }
            input.PopLimit(limit);

            // No items gives the proto3 default, an empty collection.
            items.Add(new KeyValuePair<K, V>(key, m_Builder(values ?? new List<J>())));
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ValueReader<K> m_Key;

        private readonly K m_KeyDefault;

        private readonly ItemReader<J> m_Items;

        private readonly Func<List<J>, V> m_Builder;

        /***************************************************/
    }
}
