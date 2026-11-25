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
using System.IO;
using System.Linq;
using System.Reflection;
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

        [Description("Converts a BHoM object to Protocol Buffers bytes, following the generated .proto definitions. \n" +
            "The bytes do not say which message they hold: the reader needs to know the type, for example from the gRPC service definition. Use ToProtobufAny to include the type.")]
        [Input("obj", "The object to convert. Its type must be part of the generated definitions.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the type of the object is used.")]
        [Output("bytes", "The protobuf encoded object, readable by any client generated from the .proto files.")]
        public static byte[] ToProtobuf(this IObject obj, string schemaFile = "")
        {
            if (obj == null)
            {
                Base.Compute.RecordError("Cannot convert a null object to protobuf.");
                return null;
            }

            IMessagePlan plan = WritePlans.Get(obj.GetType(), schemaFile);
            return plan == null ? null : WritePlans.Encode(plan, obj);
        }

        /***************************************************/

        [Description("Converts a BHoM object to Protocol Buffers bytes wrapped in a google.protobuf.Any, whose type url names the message. \n" +
            "This allows the object to be read back without knowing its type up front.")]
        [Input("obj", "The object to convert. Its type must be part of the generated definitions.")]
        [Input("schemaFile", "Path of the compiled descriptor set (.protoset) of the generated definitions. If empty, the installed schema holding the type of the object is used.")]
        [Output("bytes", "The protobuf encoded google.protobuf.Any holding the object.")]
        public static byte[] ToProtobufAny(this IObject obj, string schemaFile = "")
        {
            if (obj == null)
            {
                Base.Compute.RecordError("Cannot convert a null object to protobuf.");
                return null;
            }

            IMessagePlan plan = WritePlans.Get(obj.GetType(), schemaFile);
            return plan == null ? null : WritePlans.EncodeAny(plan, obj);
        }

        /***************************************************/
    }
}
