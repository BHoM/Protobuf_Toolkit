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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BH.Engine.Adapters.Protobuf.Objects;
using BH.oM.Base;
using BH.oM.Base.Attributes;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf
{
    public static partial class Convert
    {
        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        [Description("Converts Protocol Buffers bytes back to a BHoM object, following the generated .proto definitions. \n" +
            "Fields that are absent get the proto3 default (zero, empty or null), as for any other protobuf reader.")]
        [Input("bytes", "The protobuf encoded object.")]
        [Input("type", "The C# type of the encoded message.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the type is used.")]
        [Output("obj", "The BHoM object read from the bytes.")]
        public static IObject FromProtobuf(this byte[] bytes, Type type, string schemaFile = "")
        {
            if (bytes == null)
            {
                Base.Compute.RecordError("Cannot read an object from null protobuf bytes.");
                return null;
            }

            if (type == null)
            {
                Base.Compute.RecordError("The type of the encoded message is required to read protobuf bytes. Use FromProtobufAny for bytes written with ToProtobufAny.");
                return null;
            }

            IReadPlan plan = ReadPlans.Get(type, schemaFile);
            return plan == null ? null : ReadPlans.Read(plan, bytes) as IObject;
        }

        /***************************************************/

        [Description("Converts Protocol Buffers bytes holding a google.protobuf.Any back to a BHoM object. The type is taken from the type url of the Any, so it does not need to be known up front.")]
        [Input("bytes", "The protobuf encoded google.protobuf.Any, for example written by ToProtobufAny.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the message named by the type url is used.")]
        [Output("obj", "The BHoM object read from the bytes.")]
        public static IObject FromProtobufAny(this byte[] bytes, string schemaFile = "")
        {
            if (bytes == null)
            {
                Base.Compute.RecordError("Cannot read an object from null protobuf bytes.");
                return null;
            }

            ProtoSchema schema = AnySchema(bytes, schemaFile);
            if (schema == null)
                return null;

            object result = ReadPlans.ReadAny(new ProtoReader(bytes), schema, null);
            if (result != null && !(result is IObject))
                Base.Compute.RecordWarning($"The google.protobuf.Any holds a {result.GetType().Name}, not a BHoM object.");

            return result as IObject;
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        // The schema holding the message named by the type url of an Any: the given file, or else the registered schema holding it.
        private static ProtoSchema AnySchema(byte[] bytes, string schemaFile)
        {
            string messageName = AnyMessageName(bytes);
            if (string.IsNullOrEmpty(messageName))
            {
                Base.Compute.RecordError("The bytes do not hold a google.protobuf.Any with a type url.");
                return null;
            }

            ProtoSchema schema = ProtoSchema.FindByMessage(messageName, schemaFile);
            if (schema == null)
                Base.Compute.RecordError($"None of the schemas installed in {Query.SchemaFolder()} holds the protobuf message {messageName}.");

            return schema;
        }

        /***************************************************/

        // The protobuf message named by the type url of an Any, e.g. bhom.disciplines.Bar.
        private static string AnyMessageName(byte[] bytes)
        {
            try
            {
                // Only the type url is needed: skip the value instead of copying it.
                string typeUrl = "";
                CodedInputStream input = new CodedInputStream(bytes);
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if (WireFormat.GetTagFieldNumber(tag) == 1)
                        typeUrl = input.ReadString();
                    else
                        input.SkipLastField();
                }
                return typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
            }
            catch (InvalidProtocolBufferException)
            {
                return null;
            }
        }

        /***************************************************/
    }
}
