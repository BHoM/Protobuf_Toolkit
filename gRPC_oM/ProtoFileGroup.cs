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

namespace BH.oM.Adapters.gRPC
{
    [Description("A set of assemblies and/or namespaces whose types are written to one shared .proto file.")]
    public class ProtoFileGroup : IObject
    {
        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        [Description("Name of the group. Used as the file name and the last part of the package name, for example disciplines gives bhom/disciplines.proto.")]
        public virtual string Name { get; set; } = "";

        [Description("Names of the assemblies whose types belong to this group, for example Structure_oM.")]
        public virtual List<string> Assemblies { get; set; } = new List<string>();

        [Description("Namespace segments that place a type in this group regardless of its assembly. \n" +
            "For example Requests places BH.oM.Data.Requests and BH.oM.Structure.Requests types in this group. Namespace segments take precedence over assemblies.")]
        public virtual List<string> NamespaceSegments { get; set; } = new List<string>();

        /***************************************************/
    }
}
