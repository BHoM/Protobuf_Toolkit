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
using BH.oM.Base;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Reads a value written as a google.protobuf.Any: with the plan of the message named by its type url, or as a well-known type, and checks that it fits the declared type.")]
    public sealed class AnyReader<V> : ValueReader<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public AnyReader(ProtoSchema schema)
        {
            m_Schema = schema;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override V Read(ProtoReader input, WireFormat.WireType wireType)
        {
            int limit = input.PushLimit(input.ReadLength());
            object value = ReadPlans.ReadAny(input, m_Schema, typeof(V));
            input.PopLimit(limit);
            return value is V v ? v : default(V);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly ProtoSchema m_Schema;

        /***************************************************/
    }
}
