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
    [Description("Settings controlling which oM types are turned into Protocol Buffers (.proto) definitions and how they are split into files.")]
    public class ProtoConfig : IObject
    {
        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        [Description("Organisations to include. Only assemblies whose AssemblyDescriptionAttribute links to github.com/<organisation>/ are included.")]
        public virtual List<string> OrganisationsToInclude { get; set; } = new List<string> { "BHoM" };

        [Description("Repositories to include, given as organisation/repository, for example BHoM/BHoM for the core object model. \n" +
            "Only assemblies whose AssemblyDescriptionAttribute points to one of these repositories are included. Leave empty to include every repository of the organisations above.")]
        public virtual List<string> RepositoriesToInclude { get; set; } = new List<string>();

        [Description("Root package and folder name of the generated files. A file for the Structure_oM assembly is written to <RootPackage>/structure.proto in package <RootPackage>.structure.")]
        public virtual string RootPackage { get; set; } = "bhom";

        [Description("Groups of assemblies and namespaces that are written to one shared file instead of one file per assembly. \n" +
            "Grouping assemblies whose interfaces are implemented in each other avoids circular imports between the generated files.")]
        public virtual List<ProtoFileGroup> FileGroups { get; set; } = new List<ProtoFileGroup>();

        [Description("Full names of interfaces or abstract classes whose properties are left out when the implementations live in other files, for example BH.oM.Base.IFragment. \n" +
            "The field number of a skipped property is reserved so the property can be added later without breaking compatibility.")]
        public virtual List<string> SkippedUnionTypes { get; set; } = new List<string>();

        [Description("If true, a property typed as an interface whose implementations live in the file of the property owner and its dependencies gets a union defined in the owner's file, rather than falling back to google.protobuf.Any.")]
        public virtual bool ConsumerSideUnions { get; set; } = true;

        /***************************************************/
    }
}
