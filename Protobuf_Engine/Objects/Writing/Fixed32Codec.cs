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
    [Description("Writes a fixed32 field, e.g. a colour as ARGB.")]
    public sealed class Fixed32Codec : TaggedCodec<uint>, IPackableCodec<uint>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public Fixed32Codec(int number, bool force) : base(number, WireFormat.WireType.Fixed32)
        {
            m_Force = force;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(uint value, WriteContext context) => m_Force || value != 0 ? TagSize + 4 : 0;

        /***************************************************/

        public override void Write(uint value, WriteContext context)
        {
            if (m_Force || value != 0)
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteFixed32(value);
            }
        }

        /***************************************************/

        public int ValueSize(uint value) => 4;

        /***************************************************/

        public void WriteValue(uint value, WriteContext context)
        {
            context.Output.WriteFixed32(value);
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly bool m_Force;

        /***************************************************/
    }
}
