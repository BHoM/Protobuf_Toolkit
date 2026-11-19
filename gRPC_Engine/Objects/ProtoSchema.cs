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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.gRPC
{
    // A compiled set of generated .proto files (a protoc descriptor set), read through the bhom custom options
    // that link messages and fields back to the C# types and properties they represent.
    internal class ProtoSchema
    {
        /***************************************************/
        /**** Custom options                            ****/
        /***************************************************/

        // Numbers of the custom options declared in the generated <root>/options.proto.
        internal const int CSharpTypeOption = 50001;
        internal const int ReservedMemberOption = 50002;
        internal const int PropertyOption = 50001;
        internal const int CSharpEnumOption = 50001;

        // option (bhom.csharp_type) = "BH.oM.Structure.Elements.Bar";
        internal static readonly Extension<MessageOptions, string> CSharpType =
            new Extension<MessageOptions, string>(CSharpTypeOption, FieldCodec.ForString(WireFormat.MakeTag(CSharpTypeOption, WireFormat.WireType.LengthDelimited)));

        // option (bhom.reserved_member) = "Fragments=3";
        internal static readonly RepeatedExtension<MessageOptions, string> ReservedMember =
            new RepeatedExtension<MessageOptions, string>(ReservedMemberOption, FieldCodec.ForString(WireFormat.MakeTag(ReservedMemberOption, WireFormat.WireType.LengthDelimited)));

        // [(bhom.property) = "Start"]
        internal static readonly Extension<FieldOptions, string> Property =
            new Extension<FieldOptions, string>(PropertyOption, FieldCodec.ForString(WireFormat.MakeTag(PropertyOption, WireFormat.WireType.LengthDelimited)));

        // option (bhom.csharp_enum) = "BH.oM.Structure.Elements.BarFEAType";
        internal static readonly Extension<EnumOptions, string> CSharpEnum =
            new Extension<EnumOptions, string>(CSharpEnumOption, FieldCodec.ForString(WireFormat.MakeTag(CSharpEnumOption, WireFormat.WireType.LengthDelimited)));

        internal const string AnyMessageName = "google.protobuf.Any";

        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        public string FilePath { get; private set; } = "";

        // All messages of the bhom files, by protobuf full name (package.Message).
        public Dictionary<string, MessageDescriptor> MessagesByName { get; } = new Dictionary<string, MessageDescriptor>();

        // Messages of concrete C# classes, by C# type full name.
        public Dictionary<string, MessageDescriptor> MessagesByType { get; } = new Dictionary<string, MessageDescriptor>();

        /***************************************************/
        /**** Loading                                   ****/
        /***************************************************/

        public static ProtoSchema FromBytes(byte[] bytes, string filePath = "")
        {
            FileDescriptorSet set = FileDescriptorSet.Parser.ParseFrom(bytes);
            ExtensionRegistry registry = new ExtensionRegistry { CSharpType, ReservedMember, Property, CSharpEnum };

            // protoc writes the files in dependency order, which is the order BuildFromByteStrings needs.
            IReadOnlyList<FileDescriptor> files = FileDescriptor.BuildFromByteStrings(set.File.Select(f => f.ToByteString()), registry);

            ProtoSchema schema = new ProtoSchema { FilePath = filePath };
            foreach (FileDescriptor file in files.Where(f => !f.Name.StartsWith("google/")))
            {
                foreach (MessageDescriptor message in file.MessageTypes)
                {
                    schema.MessagesByName[message.FullName] = message;
                    string typeName = CSharpTypeOf(message);
                    if (typeName != null && !IsUnion(message) && !schema.MessagesByType.ContainsKey(typeName))
                        schema.MessagesByType[typeName] = message;
                }
            }
            return schema;
        }

        /***************************************************/

        // Loads a descriptor set file, cached until the file changes.
        public static ProtoSchema Load(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                Base.Compute.RecordError($"No protobuf descriptor set found at {fullPath}.");
                return null;
            }

            DateTime timestamp = File.GetLastWriteTimeUtc(fullPath);
            if (m_Cache.TryGetValue(fullPath, out (DateTime Timestamp, ProtoSchema Schema) cached) && cached.Timestamp == timestamp)
                return cached.Schema;

            ProtoSchema schema;
            try
            {
                schema = FromBytes(File.ReadAllBytes(fullPath), fullPath);
            }
            catch (Exception e)
            {
                Base.Compute.RecordError($"{fullPath} is not a valid protobuf descriptor set: {e.Message}");
                return null;
            }

            m_Cache[fullPath] = (timestamp, schema);
            return schema;
        }

        /***************************************************/

        // The descriptor sets installed in the BHoM resources folder.
        public static List<ProtoSchema> Installed()
        {
            return Query.InstalledSchemaFiles().Select(Load).Where(s => s != null).ToList();
        }

        /***************************************************/

        // The schema to use for a type: the given file, or else the installed schema holding the type.
        public static ProtoSchema Find(Type type, string schemaFile, bool silent = false)
        {
            ProtoSchema schema = string.IsNullOrWhiteSpace(schemaFile)
                ? Installed().FirstOrDefault(s => s.MessagesByType.ContainsKey(type.FullName))
                : Load(schemaFile);

            if (schema == null || !schema.MessagesByType.ContainsKey(type.FullName))
            {
                if (!silent)
                {
                    string source = string.IsNullOrWhiteSpace(schemaFile) ? $"the schemas installed in {Query.SchemaFolder()}" : schemaFile;
                    Base.Compute.RecordError($"{type.FullName} has no protobuf definition in {source}.");
                }
                return null;
            }

            return schema;
        }

        /***************************************************/

        // The schema holding a protobuf message: the given file, or else the installed schema holding the message.
        public static ProtoSchema FindByMessage(string messageName, string schemaFile)
        {
            if (!string.IsNullOrWhiteSpace(schemaFile))
                return Load(schemaFile);

            return Installed().FirstOrDefault(s => s.MessagesByName.ContainsKey(messageName));
        }

        /***************************************************/
        /**** Lookups                                   ****/
        /***************************************************/

        public static string CSharpTypeOf(MessageDescriptor message)
        {
            MessageOptions options = message?.GetOptions();
            return options != null && options.HasExtension(CSharpType) ? options.GetExtension(CSharpType) : null;
        }

        /***************************************************/

        public static string PropertyOf(FieldDescriptor field)
        {
            FieldOptions options = field?.GetOptions();
            return options != null && options.HasExtension(Property) ? options.GetExtension(Property) : null;
        }

        /***************************************************/

        // Unions generated from interfaces and abstract classes hold all their fields in a oneof called 'value'.
        public static bool IsUnion(MessageDescriptor message)
        {
            return message.Oneofs.Any(o => !o.IsSynthetic && o.Name == "value");
        }

        /***************************************************/

        public static bool IsAny(MessageDescriptor message)
        {
            return message?.FullName == AnyMessageName;
        }

        /***************************************************/

        public MessageDescriptor MessageOf(Type type)
        {
            return type != null && MessagesByType.TryGetValue(type.FullName, out MessageDescriptor message) ? message : null;
        }

        /***************************************************/

        public Type TypeOf(MessageDescriptor message)
        {
            return m_Types.GetOrAdd(message, m => ProtoTypes.ResolveType(CSharpTypeOf(m)));
        }

        /***************************************************/

        // Property name -> field, for the fields linked to a C# property.
        public Dictionary<string, FieldDescriptor> PropertyFields(MessageDescriptor message)
        {
            return m_PropertyFields.GetOrAdd(message, m => m.Fields.InFieldNumberOrder()
                .Select(f => (Field: f, Property: PropertyOf(f)))
                .Where(x => x.Property != null)
                .GroupBy(x => x.Property)
                .ToDictionary(g => g.Key, g => g.First().Field));
        }

        /***************************************************/

        // The field of a union holding a given implementation, or null when it goes in the 'other' slot.
        public FieldDescriptor UnionSlot(MessageDescriptor union, string typeName)
        {
            Dictionary<string, FieldDescriptor> slots = m_UnionSlots.GetOrAdd(union, u => u.Fields.InFieldNumberOrder()
                .Where(f => f.FieldType == FieldType.Message && CSharpTypeOf(f.MessageType) != null)
                .GroupBy(f => CSharpTypeOf(f.MessageType))
                .ToDictionary(g => g.Key, g => g.First()));

            return slots.TryGetValue(typeName, out FieldDescriptor field) ? field : null;
        }

        /***************************************************/

        // Field numbers by C# type and member, including reserved ones, used to keep numbers stable when regenerating.
        public Dictionary<string, Dictionary<string, int>> FieldNumberRegistry()
        {
            Dictionary<string, Dictionary<string, int>> registry = new Dictionary<string, Dictionary<string, int>>();
            foreach (MessageDescriptor message in MessagesByName.Values)
            {
                string typeName = CSharpTypeOf(message);
                if (typeName == null)
                    continue;

                if (!registry.TryGetValue(typeName, out Dictionary<string, int> numbers))
                    registry[typeName] = numbers = new Dictionary<string, int>();

                bool isUnion = IsUnion(message);
                foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
                {
                    string member = isUnion ? (field.FieldType == FieldType.Message ? CSharpTypeOf(field.MessageType) : null) : PropertyOf(field);
                    if (member != null)
                        numbers[member] = field.FieldNumber;
                }

                IEnumerable<string> reservedMembers = message.GetOptions()?.GetExtension(ReservedMember);
                if (reservedMembers == null)
                    continue; // An absent repeated option comes back as null.

                foreach (string reserved in reservedMembers)
                {
                    // Member names can contain '=', e.g. generic type names with assembly versions, so split on the last one.
                    int index = reserved.LastIndexOf('=');
                    if (index > 0 && int.TryParse(reserved.Substring(index + 1), out int number))
                        numbers[reserved.Substring(0, index)] = number;
                }
            }
            return registry;
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private static readonly ConcurrentDictionary<string, (DateTime Timestamp, ProtoSchema Schema)> m_Cache = new ConcurrentDictionary<string, (DateTime, ProtoSchema)>();

        private readonly ConcurrentDictionary<MessageDescriptor, Type> m_Types = new ConcurrentDictionary<MessageDescriptor, Type>();

        private readonly ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>> m_PropertyFields = new ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>>();

        private readonly ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>> m_UnionSlots = new ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>>();

        /***************************************************/
    }
}
