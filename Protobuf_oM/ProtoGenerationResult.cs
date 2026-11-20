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
    [Description("Output of a Protocol Buffers generation run.")]
    public class ProtoGenerationResult : IObject
    {
        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        [Description("The generated .proto files, including options.proto declaring the custom options that link messages and fields to the C# types and properties.")]
        public virtual List<ProtoFile> Files { get; set; } = new List<ProtoFile>();

        [Description("Properties typed as interfaces or abstract classes with implementations in other files, and how each was handled.")]
        public virtual List<PropertyResolution> Resolutions { get; set; } = new List<PropertyResolution>();

        [Description("Import cycles between the generated files, each given as the list of file paths involved. Empty when the files can be compiled.")]
        public virtual List<List<string>> ImportCycles { get; set; } = new List<List<string>>();

        /***************************************************/
    }
}
