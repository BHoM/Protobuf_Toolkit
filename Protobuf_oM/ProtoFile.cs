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

using System.Collections.Generic;
using System.ComponentModel;
using BH.oM.Base;

namespace BH.oM.Adapters.Protobuf
{
    [Description("A generated Protocol Buffers (.proto) file.")]
    public class ProtoFile : IObject
    {
        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        [Description("Path of the file relative to the root of the proto folder, for example bhom/structure.proto. This is also the path used to import the file.")]
        public virtual string Path { get; set; } = "";

        [Description("Protocol Buffers package of the file, for example bhom.structure.")]
        public virtual string Package { get; set; } = "";

        [Description("Paths of the other files this file imports.")]
        public virtual List<string> Imports { get; set; } = new List<string>();

        [Description("Names of the assemblies whose types are defined in this file.")]
        public virtual List<string> Assemblies { get; set; } = new List<string>();

        [Description("Full text content of the .proto file.")]
        public virtual string Content { get; set; } = "";

        /***************************************************/
    }
}
