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
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Writes a value that does not match its field: left out with a warning.")]
    public sealed class MismatchCodec<V> : Codec<V>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public MismatchCodec(FieldDescriptor field)
        {
            m_Field = field;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(V value, WriteContext context)
        {
            if (value != null)
                Base.Compute.RecordWarning($"A value of type {typeof(V).Name} does not match the field {m_Field.FullName} and has been left out.");
            return 0;
        }

        /***************************************************/

        public override void Write(V value, WriteContext context) { }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly FieldDescriptor m_Field;

        /***************************************************/
    }
}
