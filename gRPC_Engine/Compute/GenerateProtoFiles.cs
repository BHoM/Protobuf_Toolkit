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
using System.Linq;
using System.Reflection;
using BH.oM.Adapters.gRPC;
using BH.oM.Base.Attributes;

namespace BH.Engine.Adapters.gRPC
{
    public static partial class Compute
    {
        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        [Description("Generates Protocol Buffers (.proto) definitions for the oM types in the given assemblies. \n" +
            "Messages and fields carry custom options naming the C# types and properties they represent. \n" +
            "Field numbers are taken from the previous schema where available, so regenerating never changes or reuses a number that has already been published.")]
        [Input("assemblies", "The oM assemblies to generate definitions for. Implementations of interfaces are only searched for in these assemblies.")]
        [Input("config", "Settings controlling the scope and the split into files. Uses the default settings if null.")]
        [Input("existingSchemaFile", "Path of the compiled descriptor set (.protoset) of the previous generation, holding the field numbers to keep. Leave empty for a first generation.")]
        [Output("result", "The generated files and how properties typed as interfaces were handled.")]
        public static ProtoGenerationResult GenerateProtoFiles(List<Assembly> assemblies, ProtoConfig config = null, string existingSchemaFile = "")
        {
            if (assemblies == null || assemblies.Count == 0)
            {
                Base.Compute.RecordError("No assemblies provided to generate protobuf definitions for.");
                return null;
            }

            config = config ?? new ProtoConfig();
            List<Assembly> inScope = assemblies.Where(a => a != null && a.IsInScope(config)).Distinct().ToList();
            if (inScope.Count == 0)
            {
                Base.Compute.RecordError("None of the provided assemblies are within the organisations and repositories of the config.");
                return null;
            }

            Dictionary<string, Dictionary<string, int>> existingNumbers = null;
            if (!string.IsNullOrWhiteSpace(existingSchemaFile))
            {
                ProtoSchema existing = ProtoSchema.Load(existingSchemaFile);
                if (existing == null)
                {
                    Base.Compute.RecordError("The previous schema could not be read. Generating with it is required to keep the published field numbers.");
                    return null;
                }
                existingNumbers = existing.FieldNumberRegistry();
            }

            ProtoGenerationResult result = new ProtoGenerator(inScope, config, existingNumbers).Generate();

            foreach (List<string> cycle in result.ImportCycles)
                Base.Compute.RecordError($"The generated files import each other in a cycle and cannot be compiled: {string.Join(", ", cycle)}. Add them to a shared ProtoFileGroup.");

            return result;
        }

        /***************************************************/
    }
}
