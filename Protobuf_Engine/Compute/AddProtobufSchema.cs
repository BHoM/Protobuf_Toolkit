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
using BH.Engine.Adapters.Protobuf.Objects;
using BH.oM.Base.Attributes;

namespace BH.Engine.Adapters.Protobuf
{
    public static partial class Compute
    {
        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        [Description("Adds a compiled protobuf schema (.protoset descriptor set) to the schemas used when no schema file is given, for example the schema of a toolkit stored outside the BHoM resources folder. \n" +
            "The schemas installed in the BHoM resources folder are registered the first time any schema is needed. A schema added afterwards only adds the types and messages that are not registered yet: it never replaces a schema already in use. \n" +
            "Schemas are read once and kept for the rest of the process, as they must match the oM assemblies loaded, which cannot change while it runs.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) to add.")]
        [Output("success", "True if the schema could be read and added, including when it was already registered.")]
        public static bool AddProtobufSchema(string schemaFile)
        {
            if (string.IsNullOrWhiteSpace(schemaFile))
            {
                Base.Compute.RecordError("A schema file is required to add a protobuf schema.");
                return false;
            }

            if (!ProtoSchema.Add(schemaFile, out int alreadyRegistered))
                return false;

            if (alreadyRegistered > 0)
                Base.Compute.RecordNote($"{alreadyRegistered} types of {schemaFile} already have a registered protobuf schema, which is kept for them.");

            return true;
        }

        /***************************************************/
    }
}
