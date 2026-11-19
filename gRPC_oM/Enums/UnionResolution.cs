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

using System.ComponentModel;

namespace BH.oM.Adapters.gRPC
{
    [Description("How a property typed as an interface or abstract class with implementations in other files is represented.")]
    public enum UnionResolution
    {
        [Description("Not set.")]
        Undefined,
        [Description("A union defined in the file of the property owner, covering every implementation.")]
        ConsumerSideUnion,
        [Description("The union of the interface's own file: typed slots for implementations in that file, plus a google.protobuf.Any slot for the rest.")]
        TypedWithOther,
        [Description("A plain google.protobuf.Any.")]
        Any,
        [Description("The property is left out and its field number reserved.")]
        Skipped
    }
}
