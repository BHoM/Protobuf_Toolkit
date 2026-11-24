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
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using BH.oM.Base.Attributes;

namespace BH.Engine.Adapters.Protobuf
{
    public static partial class Query
    {
        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        [Description("Returns the folder the compiled protobuf schemas (.protoset descriptor sets) are installed in: the Protobuf folder of the BHoM resources folder.")]
        [Output("folder", "The folder holding the installed schemas.")]
        public static string SchemaFolder()
        {
            return Path.Combine(BH.Engine.Base.Query.BHoMFolderResources(), "Protobuf");
        }

        /***************************************************/

        [Description("Returns the paths of the compiled protobuf schemas (.protoset descriptor sets) installed in the BHoM resources folder.")]
        [Output("files", "The installed schema files.")]
        public static List<string> InstalledSchemaFiles()
        {
            string folder = SchemaFolder();
            if (!Directory.Exists(folder))
                return new List<string>();

            return Directory.GetFiles(folder, "*.protoset").OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        /***************************************************/

        [Description("Checks whether a type has a protobuf definition, and can therefore be converted to and from protobuf.")]
        [Input("type", "The C# type to check.")]
        [Input("schemaFile", "Path of a compiled descriptor set (.protoset). If empty, the installed schemas are checked.")]
        [Output("isInSchema", "True if the type has a protobuf message.")]
        public static bool IsInSchema(this Type type, string schemaFile = "")
        {
            return type != null && ProtoSchema.Find(type, schemaFile, true) != null;
        }

        /***************************************************/

        [Description("Returns the protobuf message name of a type, e.g. bhom.disciplines.Bar for BH.oM.Structure.Elements.Bar.")]
        [Input("type", "The C# type to get the message name of.")]
        [Input("schemaFile", "Path of a compiled descriptor set (.protoset). If empty, the installed schemas are used.")]
        [Output("name", "The full name of the protobuf message, or an empty string if the type has no protobuf definition.")]
        public static string ProtoMessageName(this Type type, string schemaFile = "")
        {
            if (type == null)
                return "";

            return ProtoSchema.Find(type, schemaFile, true)?.MessageOf(type)?.FullName ?? "";
        }

        /***************************************************/

        [Description("Returns the properties of a type that are not part of its protobuf message, for example properties typed as interfaces implemented in other files, or as .NET types without a protobuf equivalent.")]
        [Input("type", "The C# type to check.")]
        [Input("schemaFile", "Path of a compiled descriptor set (.protoset). If empty, the installed schemas are used.")]
        [Output("properties", "Names of the properties that are not exchanged.")]
        public static List<string> NotExchangedProperties(this Type type, string schemaFile = "")
        {
            if (type == null)
                return new List<string>();

            ProtoSchema schema = ProtoSchema.Find(type, schemaFile);
            if (schema == null)
                return new List<string>();

            Dictionary<string, Google.Protobuf.Reflection.FieldDescriptor> fields = schema.PropertyFields(schema.MessageOf(type));
            return ProtoTypes.PropertiesByName(type).Keys.Where(p => !fields.ContainsKey(p)).ToList();
        }

        /***************************************************/

        [Description("Returns the full names of the C# classes that have a protobuf message in a schema.")]
        [Input("schemaFile", "Path of a compiled descriptor set (.protoset). If empty, all registered schemas are used: those installed and those added with AddProtobufSchema.")]
        [Output("types", "Full names of the C# classes with a protobuf message.")]
        public static List<string> TypesInSchema(string schemaFile = "")
        {
            IEnumerable<ProtoSchema> schemas = string.IsNullOrWhiteSpace(schemaFile)
                ? ProtoSchema.Registered()
                : new[] { ProtoSchema.Load(schemaFile) }.Where(s => s != null);

            return schemas.SelectMany(s => s.MessagesByType.Keys).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /***************************************************/
    }
}
