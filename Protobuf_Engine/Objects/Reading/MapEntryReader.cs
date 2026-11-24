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
    [Description("An entry of a dictionary: a message {1: key, 2: value}.")]
    public sealed class MapEntryReader<K, V> : ItemReader<KeyValuePair<K, V>>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public MapEntryReader(ValueReader<K> key, object keyDefault, ValueReader<V> value, object valueDefault)
        {
            m_Key = key;
            m_KeyDefault = keyDefault is K k ? k : default(K);
            m_Value = value;
            m_ValueDefault = valueDefault is V v ? v : default(V);
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override void ReadInto(List<KeyValuePair<K, V>> items, ProtoReader input, WireFormat.WireType wireType)
        {
            K key = m_KeyDefault;
            V value = default(V);
            bool hasValue = false;

            int limit = input.PushLimit(input.ReadLength());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int number = WireFormat.GetTagFieldNumber(tag);
                if (number == 1)
                    key = m_Key.Read(input, WireFormat.GetTagWireType(tag));
                else if (number == 2)
                {
                    value = m_Value.Read(input, WireFormat.GetTagWireType(tag));
                    hasValue = value != null;
                }
                else
                    input.SkipLastField();
            }
            input.PopLimit(limit);

            // An absent (or null) value gets the proto3 default.
            if (!hasValue)
                value = m_ValueDefault;

            items.Add(new KeyValuePair<K, V>(key, value));
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ValueReader<K> m_Key;

        private readonly K m_KeyDefault;

        private readonly ValueReader<V> m_Value;

        private readonly V m_ValueDefault;

        /***************************************************/
    }
}
