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
using System.ComponentModel;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("A compiled set of generated .proto files (a protoc descriptor set), read through the bhom custom options that link messages and fields back to the C# types and properties they represent.")]
    public class ProtoSchema
    {
        /***************************************************/
        /**** Custom options                            ****/
        /***************************************************/

        // Number of the custom options declared in the generated <root>/options.proto. All options of one kind (message, field, enum)
        // live in one options message, so the same single number is used for all three kinds and new options never need a new number.
        public const int InfoOption = 50000;

        // Field numbers inside the options messages MessageInfo, FieldInfo and EnumInfo.
        public const int CSharpTypeNumber = 1;      // MessageInfo.csharp_type
        public const int ReservedMemberNumber = 2;  // MessageInfo.reserved_member
        public const int PropertyNumber = 1;        // FieldInfo.property
        public const int AnyOfNumber = 2;           // FieldInfo.any_of
        public const int CSharpEnumNumber = 1;      // EnumInfo.csharp_enum

        // The option values are messages without generated C# classes. They are read as raw bytes, as a message and bytes share the
        // length-delimited wire type, and collected as a repeated field because protoc may write one message option as several chunks.

        // option (.bhom.message_info) = { csharp_type: "BH.oM.Structure.Elements.Bar" };
        public static readonly RepeatedExtension<MessageOptions, ByteString> MessageInfo =
            new RepeatedExtension<MessageOptions, ByteString>(InfoOption, FieldCodec.ForBytes(WireFormat.MakeTag(InfoOption, WireFormat.WireType.LengthDelimited)));

        // [(.bhom.field_info) = { property: "Start" }]
        public static readonly RepeatedExtension<FieldOptions, ByteString> FieldInfo =
            new RepeatedExtension<FieldOptions, ByteString>(InfoOption, FieldCodec.ForBytes(WireFormat.MakeTag(InfoOption, WireFormat.WireType.LengthDelimited)));

        // option (.bhom.enum_info) = { csharp_enum: "BH.oM.Structure.Elements.BarFEAType" };
        public static readonly RepeatedExtension<EnumOptions, ByteString> EnumInfo =
            new RepeatedExtension<EnumOptions, ByteString>(InfoOption, FieldCodec.ForBytes(WireFormat.MakeTag(InfoOption, WireFormat.WireType.LengthDelimited)));

        public const string AnyMessageName = "google.protobuf.Any";

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
            ExtensionRegistry registry = new ExtensionRegistry { MessageInfo, FieldInfo, EnumInfo };

            // protoc writes the files in dependency order, which is the order BuildFromByteStrings needs.
            IReadOnlyList<FileDescriptor> files = FileDescriptor.BuildFromByteStrings(set.File.Select(f => f.ToByteString()), registry);

            ProtoSchema schema = new ProtoSchema
            {
                FilePath = filePath
            };
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

        // A schema must match the oM assemblies loaded in the process, which cannot change during it, and published schemas
        // never change. So each schema file is read once and kept for the rest of the process: the writer and reader, which
        // ask for the schema on every call, never touch the file system again.
        public static ProtoSchema Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            if (m_ByPath.TryGetValue(filePath, out ProtoSchema schema))
                return schema;

            string fullPath = Path.GetFullPath(filePath);
            if (!m_ByPath.TryGetValue(fullPath, out schema))
            {
                schema = ReadFile(fullPath);
                if (schema == null)
                    return null;

                schema = m_ByPath.GetOrAdd(fullPath, schema); // Another thread may have read it meanwhile: keep a single instance.
            }

            m_ByPath.TryAdd(filePath, schema);
            return schema;
        }

        /***************************************************/

        // Reads a descriptor set file without caching it, e.g. for the generator, which reads the previous schema once.
        public static ProtoSchema ReadFile(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                Base.Compute.RecordError($"No protobuf descriptor set found at {fullPath}.");
                return null;
            }

            try
            {
                return FromBytes(File.ReadAllBytes(fullPath), fullPath);
            }
            catch (Exception e)
            {
                Base.Compute.RecordError($"{fullPath} is not a valid protobuf descriptor set: {e.Message}");
                return null;
            }
        }

        /***************************************************/

        /***************************************************/
        /**** Registry                                  ****/
        /***************************************************/

        // The schemas used when no schema file is given: those installed in the BHoM resources folder, registered the first
        // time any is needed, plus any added later with Add. Types and messages are looked up in the registry only, never by
        // searching files. A schema added later only adds the types and messages not registered yet: it never replaces any.
        public static List<ProtoSchema> Registered()
        {
            EnsureInstalled();
            lock (m_RegistryLock)
                return m_Registered.ToList();
        }

        /***************************************************/

        // Adds a schema to the registry. Returns the number of its types already provided by a registered schema, which stay as they were.
        public static bool Add(string filePath, out int alreadyRegistered)
        {
            EnsureInstalled();
            alreadyRegistered = 0;
            ProtoSchema schema = Load(filePath);
            if (schema == null)
                return false;

            alreadyRegistered = Register(schema);
            return true;
        }

        /***************************************************/

        // The schema to use for a type: the given file, or else the registered schema holding the type.
        public static ProtoSchema Find(Type type, string schemaFile, bool silent = false)
        {
            ProtoSchema schema;
            if (string.IsNullOrWhiteSpace(schemaFile))
            {
                EnsureInstalled();
                m_SchemaByType.TryGetValue(type.FullName, out schema);
            }
            else
            {
                schema = Load(schemaFile);
            }

            if (schema == null || !schema.MessagesByType.ContainsKey(type.FullName))
            {
                if (!silent)
                {
                    string source = string.IsNullOrWhiteSpace(schemaFile) ? $"the schemas installed in {Query.SchemaFolder()} or added with AddProtobufSchema" : schemaFile;
                    Base.Compute.RecordError($"{type.FullName} has no protobuf definition in {source}.");
                }
                return null;
            }

            return schema;
        }

        /***************************************************/

        // The schema holding a protobuf message: the given file, or else the registered schema holding the message.
        public static ProtoSchema FindByMessage(string messageName, string schemaFile)
        {
            if (!string.IsNullOrWhiteSpace(schemaFile))
                return Load(schemaFile);

            EnsureInstalled();
            return m_SchemaByMessage.TryGetValue(messageName, out ProtoSchema schema) ? schema : null;
        }

        /***************************************************/

        // Registers the installed schemas, once per process.
        private static void EnsureInstalled()
        {
            if (m_InstalledRegistered)
                return;

            lock (m_RegistryLock)
            {
                if (m_InstalledRegistered)
                    return;

                foreach (string file in Query.InstalledSchemaFiles())
                {
                    ProtoSchema schema = Load(file);
                    if (schema != null)
                        Register(schema);
                }

                m_InstalledRegistered = true;
            }
        }

        /***************************************************/

        // Adds the types and messages of a schema that are not registered yet. Returns the number of types left as they were.
        private static int Register(ProtoSchema schema)
        {
            lock (m_RegistryLock)
            {
                if (m_Registered.Contains(schema))
                    return 0;

                m_Registered.Add(schema);
                foreach (string messageName in schema.MessagesByName.Keys)
                    m_SchemaByMessage.TryAdd(messageName, schema);

                return schema.MessagesByType.Keys.Count(typeName => !m_SchemaByType.TryAdd(typeName, schema));
            }
        }

        /***************************************************/
        /**** Lookups                                   ****/
        /***************************************************/

        public static string CSharpTypeOf(MessageDescriptor message)
        {
            if (message == null)
                return null;

            return m_CSharpTypes.GetOrAdd(message, m => InfoStrings(m.GetOptions()?.GetExtension(MessageInfo), CSharpTypeNumber).LastOrDefault());
        }

        /***************************************************/

        public static string PropertyOf(FieldDescriptor field)
        {
            if (field == null)
                return null;

            return m_Properties.GetOrAdd(field, f => InfoStrings(f.GetOptions()?.GetExtension(FieldInfo), PropertyNumber).LastOrDefault());
        }

        /***************************************************/

        // The interface or abstract class the values of a google.protobuf.Any field implement, from (bhom.field_info).any_of.
        public static string AnyOfOf(FieldDescriptor field)
        {
            return field == null ? null : InfoStrings(field.GetOptions()?.GetExtension(FieldInfo), AnyOfNumber).LastOrDefault();
        }

        /***************************************************/

        public static List<string> ReservedMembersOf(MessageDescriptor message)
        {
            return message == null ? new List<string>() : InfoStrings(message.GetOptions()?.GetExtension(MessageInfo), ReservedMemberNumber);
        }

        /***************************************************/

        // String fields with a given number in the chunks of an options message, in order.
        private static List<string> InfoStrings(IEnumerable<ByteString> chunks, int number)
        {
            List<string> result = new List<string>();
            if (chunks == null)
                return result; // An absent repeated option comes back as null.

            foreach (ByteString chunk in chunks)
            {
                CodedInputStream input = chunk.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if (WireFormat.GetTagFieldNumber(tag) == number && WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
                        result.Add(input.ReadString());
                    else
                        input.SkipLastField();
                }
            }

            return result;
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

                foreach (string reserved in ReservedMembersOf(message))
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

        // Schemas read from files, by the path they were asked for with and by full path. Never re-read during the process.
        private static readonly ConcurrentDictionary<string, ProtoSchema> m_ByPath = new ConcurrentDictionary<string, ProtoSchema>(StringComparer.OrdinalIgnoreCase);

        // The registry: schemas used when no schema file is given, and the schema of each type and message.
        private static readonly object m_RegistryLock = new object();

        private static volatile bool m_InstalledRegistered;

        private static readonly List<ProtoSchema> m_Registered = new List<ProtoSchema>();

        private static readonly ConcurrentDictionary<string, ProtoSchema> m_SchemaByType = new ConcurrentDictionary<string, ProtoSchema>();

        private static readonly ConcurrentDictionary<string, ProtoSchema> m_SchemaByMessage = new ConcurrentDictionary<string, ProtoSchema>();

        private static readonly ConcurrentDictionary<MessageDescriptor, string> m_CSharpTypes = new ConcurrentDictionary<MessageDescriptor, string>();

        private static readonly ConcurrentDictionary<FieldDescriptor, string> m_Properties = new ConcurrentDictionary<FieldDescriptor, string>();

        private readonly ConcurrentDictionary<MessageDescriptor, Type> m_Types = new ConcurrentDictionary<MessageDescriptor, Type>();

        private readonly ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>> m_PropertyFields = new ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>>();

        private readonly ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>> m_UnionSlots = new ConcurrentDictionary<MessageDescriptor, Dictionary<string, FieldDescriptor>>();

        /***************************************************/
    }
}
