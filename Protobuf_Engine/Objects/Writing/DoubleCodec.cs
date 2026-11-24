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
    [Description("Writes a double field. Only +0.0 is left out as the default: -0.0 and NaN are written.")]
    public sealed class DoubleCodec : TaggedCodec<double>
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public DoubleCodec(int number, bool force) : base(number, WireFormat.WireType.Fixed64)
        {
            m_Force = force;
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public override int Size(double value, WriteContext context) => Written(value) ? TagSize + 8 : 0;

        /***************************************************/

        public override void Write(double value, WriteContext context)
        {
            if (Written(value))
            {
                context.Output.WriteTag(Tag);
                context.Output.WriteDouble(value);
            }
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        // Only +0.0 is the default and left out: -0.0 and NaN are written.
        private bool Written(double value) => m_Force || BitConverter.DoubleToInt64Bits(value) != 0;

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly bool m_Force;

        /***************************************************/
    }
}
