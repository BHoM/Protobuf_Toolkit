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
    [Description("Writes a float field. Only +0.0 is left out as the default: -0.0 and NaN are written.")]
    public sealed class FloatCodec : TaggedCodec<float>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public FloatCodec(int number, bool force) : base(number, WireFormat.WireType.Fixed32)
        {
            m_Force = force;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(float value, WriteContext context) => Written(value) ? TagSize + 4 : 0;

        /***************************************************/

        public override void Write(float value, WriteContext context)
        {
            if (Written(value))
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteFloat(value);
            }
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private bool Written(float value) => m_Force || value != 0 || float.IsNegativeInfinity(1 / value);

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly bool m_Force;

        /***************************************************/
    }
}
