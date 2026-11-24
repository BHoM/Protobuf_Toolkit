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
    [Description("Base of the readers of a property of a C# type: reads the occurrences of its field, then sets the property, or keeps the value for types built with their constructor.")]
    public abstract class FieldReader<T>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        protected FieldReader(int index)
        {
            Index = index;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        // Reads one occurrence of the field: sets the property, or keeps the value for types built with their constructor.
        public abstract void Read(T obj, ReadState state, ProtoReader input, WireFormat.WireType wireType);

        /***************************************************/

        // After the last field: builds repeated values (defaults == false), or sets the default of absent fields (defaults == true).
        public abstract void Finish(T obj, ReadState state, bool defaults);

        /***************************************************/

        public abstract void SetBoxed(T obj, object value);

        /***************************************************/
        /**** Public Fields                             ****/
        /***************************************************/

        public readonly int Index;

        /***************************************************/
    }
}
