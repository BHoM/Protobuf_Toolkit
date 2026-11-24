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
using System.Linq;
using System.Reflection;
using System.Text;
using BH.oM.Adapters.Protobuf;
using BH.oM.Base;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Walks the oM types by reflection and writes .proto files, mirroring the approach of the JSONSchema_Toolkit. - Concrete classes and enums are the roots. Interfaces and abstract classes are only emitted when a property uses them. - Inheritance is flattened: each message holds all public properties, inherited ones included. - Interfaces and abstract classes become union messages with a oneof over their implementations. - Types are split into one file per assembly, or per ProtoFileGroup. Unions whose implementations live in other files   would create circular imports, so they are defined on the consumer side, given an 'Any other' slot, or skipped.   ProtoConfig.OpenUnionTypes are always google.protobuf.Any (with the any_of option naming the interface), and   ProtoConfig.ClosedUnionTypes are always the union of their own file, without an 'other' slot. - Field numbers come from the registry in the ProtoSchemaMap and are never changed once assigned.")]
    public class ProtoGenerator
    {
        /***************************************************/
        /**** Private Classes                           ****/
        /***************************************************/

        private class FileBuilder
        {
            public string Key = "";
            public string Package = "";
            public string Path = "";
            public SortedDictionary<string, string> Definitions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            public SortedDictionary<string, string> Wrappers = new SortedDictionary<string, string>(StringComparer.Ordinal);
            public SortedSet<string> Imports = new SortedSet<string>(StringComparer.Ordinal);
            public SortedSet<string> Assemblies = new SortedSet<string>(StringComparer.Ordinal);
            public HashSet<string> UsedNames = new HashSet<string>();
        }

        private class SkipPropertyException : Exception
        {
            public Type UnionType { get; }
            public SkipPropertyException(Type unionType) { UnionType = unionType; }
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly List<Assembly> m_Assemblies;
        private readonly ProtoConfig m_Config;
        private readonly Dictionary<string, Dictionary<string, int>> m_Registry;
        private readonly List<Type> m_AllTypes;
        private readonly Queue<Type> m_ToDo = new Queue<Type>();
        private readonly HashSet<Type> m_Seen = new HashSet<Type>();
        private readonly Dictionary<Type, string> m_Names = new Dictionary<Type, string>();
        private readonly Dictionary<string, FileBuilder> m_Files = new Dictionary<string, FileBuilder>();
        private readonly Dictionary<Type, List<Type>> m_Subtypes = new Dictionary<Type, List<Type>>();
        private readonly Dictionary<string, HashSet<string>> m_ClashingNames = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<(Type, string), string> m_ConsumerUnions = new Dictionary<(Type, string), string>();
        private readonly List<PropertyResolution> m_Resolutions = new List<PropertyResolution>();
        private Dictionary<string, HashSet<string>> m_FileDependencies;
        private FileBuilder m_Current;
        private Type m_CurrentOwner;
        private PropertyInfo m_CurrentProperty;
        private string m_CurrentAnyOf; // Interface of the google.protobuf.Any field being generated, written as the any_of option.

        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public ProtoGenerator(List<Assembly> assemblies, ProtoConfig config, Dictionary<string, Dictionary<string, int>> existingNumbers)
        {
            m_Assemblies = assemblies;
            m_Config = config;

            // Only the field numbers carry over from the previous schema: everything else is regenerated.
            m_Registry = new Dictionary<string, Dictionary<string, int>>();
            if (existingNumbers != null)
                foreach (KeyValuePair<string, Dictionary<string, int>> kvp in existingNumbers)
                    m_Registry[kvp.Key] = new Dictionary<string, int>(kvp.Value);

            m_AllTypes = assemblies.SelectMany(SafeTypes)
                .Where(t => ProtoTypes.IsOmType(t) && !t.ContainsGenericParameters)
                .ToList();

            FindClashingNames();
        }

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public ProtoGenerationResult Generate()
        {
            List<Type> roots = m_AllTypes
                .Where(t => !(t.IsAbstract && t.IsSealed) && !typeof(Attribute).IsAssignableFrom(t))
                .Where(t => t.IsEnum || (t.IsClass && !t.IsAbstract && typeof(IObject).IsAssignableFrom(t)))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (Type root in roots)
                Enqueue(root);

            while (m_ToDo.Count > 0)
            {
                Type type = m_ToDo.Dequeue();
                m_Current = FileOf(type);
                m_Current.Assemblies.Add(type.Assembly.GetName().Name);
                try
                {
                    if (type.IsEnum)
                        m_Current.Definitions[type.FullName] = EnumDefinition(type);
                    else if (ProtoTypes.IsUnion(type))
                        m_Current.Definitions[type.FullName] = UnionDefinition(type);
                    else
                        m_Current.Definitions[type.FullName] = MessageDefinition(type);
                }
                catch (Exception e)
                {
                    BH.Engine.Base.Compute.RecordWarning($"Could not generate a definition for {type.FullName}: {e.Message}");
                }
            }

            List<FileBuilder> files = m_Files.Values.Where(f => f.Definitions.Count > 0).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();

            return new ProtoGenerationResult
            {
                Files = new[] { OptionsFile() }.Concat(files.Select(ToProtoFile)).ToList(),
                Resolutions = m_Resolutions,
                ImportCycles = FindCycles(files.ToDictionary(f => f.Path, f => f.Imports.Where(i => !i.StartsWith("google/")).ToList()))
            };
        }

        /***************************************************/
        /**** Private Methods - Files                   ****/
        /***************************************************/

        private string FileKey(Type type)
        {
            // A closed generic belongs with the assembly that depends on all the others,
            // e.g. BHoMGroup<Bar> belongs with Structure_oM rather than BHoM.
            if (type.IsConstructedGenericType)
                return AssemblyKey(HomeAssembly(type));

            if (type.Namespace != null)
            {
                string[] segments = type.Namespace.Split('.');
                ProtoFileGroup group = m_Config.FileGroups.FirstOrDefault(g => g.NamespaceSegments.Any(s => segments.Contains(s)));
                if (group != null)
                    return group.Name;
            }

            return AssemblyKey(type.Assembly);
        }

        /***************************************************/

        private string AssemblyKey(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            ProtoFileGroup group = m_Config.FileGroups.FirstOrDefault(g => g.Assemblies.Contains(name));
            return group?.Name ?? name;
        }

        /***************************************************/

        private static Assembly HomeAssembly(Type type)
        {
            if (!type.IsConstructedGenericType)
                return type.Assembly;

            List<Assembly> candidates = new[] { type.Assembly }
                .Concat(type.GetGenericArguments().Select(HomeAssembly))
                .Where(a => a.GetName().Name != "System.Private.CoreLib" && a.GetName().Name != "mscorlib" && a.GetName().Name != "netstandard")
                .Distinct()
                .ToList();

            return candidates.FirstOrDefault(c => candidates.All(o => c == o || c.GetReferencedAssemblies().Any(r => r.Name == o.GetName().Name))) ?? type.Assembly;
        }

        /***************************************************/

        private FileBuilder FileOf(Type type)
        {
            string key = FileKey(type);
            if (m_Files.TryGetValue(key, out FileBuilder file))
                return file;

            string shortName = key == "BHoM" ? "base" : key.Replace("_oM", "").Replace(".", "_").ToLowerInvariant();
            file = new FileBuilder
            {
                Key = key,
                Package = m_Config.RootPackage + "." + shortName,
                Path = m_Config.RootPackage + "/" + shortName + ".proto"
            };
            m_Files[key] = file;
            return file;
        }

        /***************************************************/

        private ProtoFile ToProtoFile(FileBuilder file)
        {
            // Every file uses the bhom custom options linking messages and fields to the C# types and properties.
            file.Imports.Add(OptionsPath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// AUTO-GENERATED by Protobuf_Toolkit from the compiled oM assemblies. Do not edit by hand.");
            sb.AppendLine("// Field numbers never change once assigned: numbers no longer in use are reserved.");
            sb.AppendLine($"// Assemblies: {string.Join(", ", file.Assemblies)}");
            sb.AppendLine("syntax = \"proto3\";");
            sb.AppendLine($"package {file.Package};");
            sb.AppendLine();
            foreach (string import in file.Imports)
                sb.AppendLine($"import \"{import}\";");
            if (file.Imports.Count > 0)
                sb.AppendLine();

            foreach (string definition in file.Definitions.Values)
            {
                sb.Append(definition);
                sb.AppendLine();
            }

            if (file.Wrappers.Count > 0)
            {
                sb.AppendLine("// ---- Helper wrappers (protobuf cannot nest 'repeated' directly) ----");
                sb.AppendLine();
                foreach (string wrapper in file.Wrappers.Values)
                {
                    sb.Append(wrapper);
                    sb.AppendLine();
                }
            }

            return new ProtoFile
            {
                Path = file.Path,
                Package = file.Package,
                Imports = file.Imports.ToList(),
                Assemblies = file.Assemblies.ToList(),
                Content = sb.ToString().Replace("\r\n", "\n")
            };
        }

        /***************************************************/

        private string OptionsPath => m_Config.RootPackage + "/options.proto";

        /***************************************************/

        // Reference to one option of the options messages, e.g. (.bhom.message_info).csharp_type.
        private string Option(string kind, string name) => $"(.{m_Config.RootPackage}.{kind}).{name}";

        private string MessageOption(string name) => Option("message_info", name);

        private string EnumOption(string name) => Option("enum_info", name);

        // Options of a field: the C# property, and for google.protobuf.Any fields generated from an interface, the interface.
        private string FieldOptions(string property, string anyOf)
        {
            if (string.IsNullOrEmpty(anyOf))
                return $"[{Option("field_info", "property")} = {Quote(property)}]";

            return $"[(.{m_Config.RootPackage}.field_info) = {{ property: {Quote(property)} any_of: {Quote(anyOf)} }}]";
        }

        /***************************************************/

        private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /***************************************************/

        // The custom options linking the generated definitions back to the C# oM. Other platforms can ignore them.
        private ProtoFile OptionsFile()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// AUTO-GENERATED by Protobuf_Toolkit. Do not edit by hand.");
            sb.AppendLine("// Custom options linking the generated messages and fields to the C# BHoM types and properties they represent.");
            sb.AppendLine("// Clients on other platforms can ignore them.");
            sb.AppendLine("syntax = \"proto3\";");
            sb.AppendLine($"package {m_Config.RootPackage};");
            sb.AppendLine();
            sb.AppendLine("import \"google/protobuf/descriptor.proto\";");
            sb.AppendLine();
            sb.AppendLine("// Options on messages, e.g. option (.bhom.message_info).csharp_type = \"BH.oM.Structure.Elements.Bar\";");
            sb.AppendLine("message MessageInfo {");
            sb.AppendLine("  // Full name of the C# type the message represents. For a union, the interface or abstract class.");
            sb.AppendLine($"  string csharp_type = {ProtoSchema.CSharpTypeNumber};");
            sb.AppendLine("  // Members whose field numbers are reserved, as <property or type name>=<number>, so the numbers are never reused.");
            sb.AppendLine($"  repeated string reserved_member = {ProtoSchema.ReservedMemberNumber};");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("// Options on fields, e.g. [(.bhom.field_info).property = \"Start\"].");
            sb.AppendLine("message FieldInfo {");
            sb.AppendLine("  // Name of the C# property the field represents.");
            sb.AppendLine($"  string property = {ProtoSchema.PropertyNumber};");
            sb.AppendLine("  // For google.protobuf.Any fields: full name of the C# interface or abstract class the values implement.");
            sb.AppendLine("  // Values of other types are ignored by the BHoM reader.");
            sb.AppendLine($"  string any_of = {ProtoSchema.AnyOfNumber};");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("// Options on enums, e.g. option (.bhom.enum_info).csharp_enum = \"BH.oM.Structure.Elements.BarFEAType\";");
            sb.AppendLine("message EnumInfo {");
            sb.AppendLine("  // Full name of the C# enum the enum represents.");
            sb.AppendLine($"  string csharp_enum = {ProtoSchema.CSharpEnumNumber};");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("// One number for each kind of options, so new options never need a new number.");
            sb.AppendLine("extend google.protobuf.MessageOptions {");
            sb.AppendLine($"  MessageInfo message_info = {ProtoSchema.InfoOption};");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("extend google.protobuf.FieldOptions {");
            sb.AppendLine($"  FieldInfo field_info = {ProtoSchema.InfoOption};");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("extend google.protobuf.EnumOptions {");
            sb.AppendLine($"  EnumInfo enum_info = {ProtoSchema.InfoOption};");
            sb.AppendLine("}");

            return new ProtoFile
            {
                Path = OptionsPath,
                Package = m_Config.RootPackage,
                Imports = new List<string> { "google/protobuf/descriptor.proto" },
                Content = sb.ToString().Replace("\r\n", "\n")
            };
        }

        /***************************************************/

        // Reserves the numbers of members that are no longer part of a message, so they are never reused.
        private void Reserve(StringBuilder sb, IEnumerable<KeyValuePair<string, int>> members, string comment)
        {
            foreach (KeyValuePair<string, int> member in members.OrderBy(m => m.Value))
            {
                if (comment != null)
                    sb.AppendLine($"  // {comment}");
                sb.AppendLine($"  reserved {member.Value};");
                sb.AppendLine($"  option {MessageOption("reserved_member")} = {Quote(member.Key + "=" + member.Value)};");
            }
        }

        /***************************************************/

        // Reference to a type from the current file: qualified and imported when it lives in another file.
        private string Reference(Type type)
        {
            FileBuilder target = FileOf(type);
            if (target == m_Current)
                return Name(type);

            m_Current.Imports.Add(target.Path);
            return "." + target.Package + "." + Name(type);
        }

        /***************************************************/

        private string WellKnown(string typeName, string file)
        {
            m_Current.Imports.Add(file);
            return typeName;
        }

        /***************************************************/

        // Files a given file can import without creating a cycle: those holding the assemblies its own assemblies reference.
        private HashSet<string> Dependencies(string fileKey)
        {
            if (m_FileDependencies == null)
            {
                Dictionary<string, Assembly> byName = m_Assemblies.ToDictionary(a => a.GetName().Name);
                m_FileDependencies = new Dictionary<string, HashSet<string>>();
                Dictionary<Assembly, HashSet<string>> transitive = m_Assemblies.ToDictionary(a => a, a => TransitiveReferences(a, byName));

                foreach (Type type in m_AllTypes)
                {
                    string key = FileKey(type);
                    if (!m_FileDependencies.TryGetValue(key, out HashSet<string> set))
                        m_FileDependencies[key] = set = new HashSet<string>();

                    set.Add(AssemblyKey(type.Assembly));
                    if (transitive.TryGetValue(type.Assembly, out HashSet<string> references))
                        foreach (string reference in references)
                            set.Add(AssemblyKey(byName[reference]));
                }

                foreach (KeyValuePair<string, HashSet<string>> kvp in m_FileDependencies)
                    kvp.Value.Remove(kvp.Key);
            }

            return m_FileDependencies.TryGetValue(fileKey, out HashSet<string> result) ? result : new HashSet<string>();
        }

        /***************************************************/

        private static HashSet<string> TransitiveReferences(Assembly assembly, Dictionary<string, Assembly> byName)
        {
            HashSet<string> seen = new HashSet<string>();
            Stack<string> stack = new Stack<string>(assembly.GetReferencedAssemblies().Select(r => r.Name));
            while (stack.Count > 0)
            {
                string name = stack.Pop();
                if (!byName.ContainsKey(name) || !seen.Add(name))
                    continue;

                foreach (AssemblyName reference in byName[name].GetReferencedAssemblies())
                    stack.Push(reference.Name);
            }
            return seen;
        }

        /***************************************************/
        /**** Private Methods - Walking                 ****/
        /***************************************************/

        private void Enqueue(Type type)
        {
            if (m_Seen.Add(type))
                m_ToDo.Enqueue(type);
        }

        /***************************************************/

        private List<Type> Subtypes(Type type)
        {
            if (m_Subtypes.TryGetValue(type, out List<Type> result))
                return result;

            result = m_AllTypes
                .Where(s => type.IsAssignableFrom(s) && s.IsClass && !s.IsAbstract)
                .OrderBy(s => s.FullName, StringComparer.Ordinal)
                .ToList();
            m_Subtypes[type] = result;
            return result;
        }

        /***************************************************/

        private bool IsCrossFileUnion(Type type)
        {
            return ProtoTypes.IsUnion(type) && Subtypes(type).Any(s => FileKey(s) != FileKey(type));
        }

        /***************************************************/

        // A union with no implementations in its own file is a plain google.protobuf.Any.
        private bool IsAnyUnion(Type type)
        {
            string home = FileKey(type);
            return ProtoTypes.IsUnion(type) && !Subtypes(type).Any(s => FileKey(s) == home);
        }

        /***************************************************/

        // Interfaces always exchanged as google.protobuf.Any, e.g. IFragment.
        private bool IsOpenUnion(Type type)
        {
            return ProtoTypes.IsUnion(type) && m_Config.OpenUnionTypes != null && m_Config.OpenUnionTypes.Contains(type.FullName);
        }

        /***************************************************/

        // Interfaces always exchanged as the union of their own file, without an 'other' slot, e.g. IGeometry.
        private bool IsClosedUnion(Type type)
        {
            return ProtoTypes.IsUnion(type) && m_Config.ClosedUnionTypes != null && m_Config.ClosedUnionTypes.Contains(type.FullName);
        }

        /***************************************************/

        private bool CanDefineOnConsumerSide(Type union)
        {
            HashSet<string> dependencies = Dependencies(m_Current.Key);
            List<Type> subtypes = Subtypes(union);
            return subtypes.Count > 0 && subtypes.All(s => FileKey(s) == m_Current.Key || dependencies.Contains(FileKey(s)));
        }

        /***************************************************/

        private void Resolve(Type union, UnionResolution resolution)
        {
            if (m_CurrentOwner == null || m_CurrentProperty == null)
                return;

            m_Resolutions.Add(new PropertyResolution
            {
                TypeName = m_CurrentOwner.FullName,
                PropertyName = m_CurrentProperty.Name,
                UnionTypeName = union.FullName,
                Resolution = resolution
            });
        }

        /***************************************************/
        /**** Private Methods - Naming                  ****/
        /***************************************************/

        // Names that occur more than once within a file always get their namespace as a prefix,
        // so the name a type gets does not depend on the order the types are generated in.
        private void FindClashingNames()
        {
            foreach (IGrouping<string, Type> file in m_AllTypes.GroupBy(FileKey))
            {
                m_ClashingNames[file.Key] = new HashSet<string>(file
                    .GroupBy(SimpleName)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key));
            }
        }

        /***************************************************/

        private static string SimpleName(Type type)
        {
            string name = type.Name;
            if (type.IsGenericType)
                name = name.Split('`')[0] + "_" + string.Join("_", type.GetGenericArguments().Select(a => a.Name));
            return Ascii(name);
        }

        /***************************************************/

        private string Name(Type type)
        {
            if (m_Names.TryGetValue(type, out string name))
                return name;

            FileBuilder file = FileOf(type);
            name = SimpleName(type);
            if (m_ClashingNames.TryGetValue(file.Key, out HashSet<string> clashing) && clashing.Contains(name))
                name = Ascii(type.Namespace.Replace("BH.oM.", "").Replace(".", "_")) + "_" + name;

            file.UsedNames.Add(name);
            m_Names[type] = name;
            return name;
        }

        /***************************************************/

        private static string Snake(string name)
        {
            string original = name;
            name = Ascii(name.Replace("BHoM", "Bhom"));
            if (name.Length == 0)
                name = "u" + string.Concat(original.Select(c => ((int)c).ToString("x4")));

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                        sb.Append('_');
                    continue;
                }

                if (char.IsUpper(c) && i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '_'
                    && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                    sb.Append('_');

                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /***************************************************/

        // Protobuf identifiers must be ASCII: accents are stripped (É -> E) and anything else is dropped.
        private static string Ascii(string text)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in text.Normalize(NormalizationForm.FormD))
            {
                if (c < 128 && (char.IsLetterOrDigit(c) || c == '_'))
                    sb.Append(c);
            }
            return sb.Length > 0 && char.IsDigit(sb[0]) ? "_" + sb : sb.ToString();
        }

        /***************************************************/

        private int FieldNumber(Type owner, string member, int floor)
        {
            if (!m_Registry.TryGetValue(owner.FullName, out Dictionary<string, int> numbers))
                m_Registry[owner.FullName] = numbers = new Dictionary<string, int>();

            if (numbers.TryGetValue(member, out int number))
                return number;

            number = numbers.Count == 0 ? floor : Math.Max(floor, numbers.Values.Max() + 1);
            numbers[member] = number;
            return number;
        }

        /***************************************************/
        /**** Private Methods - Definitions             ****/
        /***************************************************/

        private string MessageDefinition(Type type)
        {
            StringBuilder sb = new StringBuilder();
            Comment(sb, type.GetCustomAttribute<DescriptionAttribute>()?.Description, "");
            sb.AppendLine($"message {Name(type)} {{");
            sb.AppendLine($"  option {MessageOption("csharp_type")} = {Quote(type.FullName)};");

            HashSet<string> current = new HashSet<string>();
            m_CurrentOwner = type;
            int firstNumber = ProtoTypes.FirstPropertyNumber(type);
            if (firstNumber == 10 && type.GetInterfaces().Any(i => i.FullName == ProtoTypes.GeometryInterfaceName))
                BH.Engine.Base.Compute.RecordWarning($"{type.FullName} implements both IGeometry and IBHoMObject. Geometry types are expected never to be BHoMObjects; its properties are numbered from 10.");

            foreach (PropertyInfo property in ProtoTypes.Properties(type))
            {
                if (!current.Add(property.Name))
                    continue; // A property hidden with 'new' in a subclass: only the first is exchanged.

                int baseIndex = ProtoTypes.BaseIndex(property.Name);
                int number = FieldNumber(type, property.Name, baseIndex >= 0 ? baseIndex + 1 : firstNumber);
                m_CurrentProperty = property;

                string fieldType;
                string note;
                m_CurrentAnyOf = null;
                try
                {
                    fieldType = FieldType(property.PropertyType, out note);
                }
                catch (SkipPropertyException skip)
                {
                    string reason = typeof(Attribute).IsAssignableFrom(skip.UnionType) ? $"{skip.UnionType.Name} is a C# attribute type"
                        : !ProtoTypes.IsOmType(skip.UnionType) ? $"{skip.UnionType.FullName} has no protobuf equivalent"
                        : $"{skip.UnionType.Name} is implemented in other files";
                    Reserve(sb, new[] { new KeyValuePair<string, int>(property.Name, number) }, $"{property.Name} is not exchanged: {reason}.");
                    continue;
                }

                string unit = QuantityText(property);
                string trailing = string.Join(" ", new[] { unit, note }.Where(x => !string.IsNullOrEmpty(x)));
                Comment(sb, property.GetCustomAttribute<DescriptionAttribute>()?.Description, "  ");
                sb.AppendLine($"  {fieldType} {Snake(property.Name)} = {number} {FieldOptions(property.Name, m_CurrentAnyOf)};{(trailing.Length > 0 ? "  // " + trailing : "")}");
            }
            m_CurrentOwner = null;
            m_CurrentProperty = null;

            // Properties removed from the C# class keep their numbers reserved.
            if (m_Registry.TryGetValue(type.FullName, out Dictionary<string, int> numbers))
                Reserve(sb, numbers.Where(n => !current.Contains(n.Key)), "No longer a property of the C# type.");

            sb.AppendLine("}");
            return sb.ToString();
        }

        /***************************************************/

        private string UnionDefinition(Type type)
        {
            List<Type> subtypes = Subtypes(type);
            StringBuilder sb = new StringBuilder();
            Comment(sb, type.GetCustomAttribute<DescriptionAttribute>()?.Description, "");
            sb.AppendLine($"// C# {(type.IsInterface ? "interface" : "abstract class")}: {type.FullName}");
            sb.AppendLine($"message {Name(type)} {{");
            sb.AppendLine($"  option {MessageOption("csharp_type")} = {Quote(type.FullName)};");
            sb.AppendLine("  oneof value {");

            HashSet<string> slots = new HashSet<string>();
            foreach (Type subtype in subtypes)
            {
                Enqueue(subtype);
                if (FileKey(subtype) != m_Current.Key)
                    continue;

                int number = FieldNumber(type, subtype.FullName, 1);
                sb.AppendLine($"    {Reference(subtype)} {Snake(Name(subtype))} = {number};");
                slots.Add(subtype.FullName);
            }

            List<Type> external = subtypes.Where(s => FileKey(s) != m_Current.Key).ToList();
            if (external.Count > 0)
            {
                List<string> files = external.Select(s => FileOf(s).Path).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
                if (IsClosedUnion(type))
                {
                    // Closed union: only the implementations of this file are exchanged.
                    sb.AppendLine($"    // Implementations defined in other files are not exchanged ({external.Count}, in {string.Join(", ", files)})");
                }
                else
                {
                    sb.AppendLine($"    // Implementations defined in other files ({external.Count}, in {string.Join(", ", files)})");
                    sb.AppendLine($"    {WellKnown("google.protobuf.Any", "google/protobuf/any.proto")} other = {ProtoTypes.OtherFieldNumber};");
                }
            }

            sb.AppendLine("  }");
            ReserveUnusedSlots(sb, type, slots);
            sb.AppendLine("}");
            return sb.ToString();
        }

        /***************************************************/

        // Numbers given to implementations of an interface are the same in every union of that interface, and never reused:
        // numbers of implementations without a slot in this union are reserved.
        private void ReserveUnusedSlots(StringBuilder sb, Type union, HashSet<string> slots)
        {
            if (m_Registry.TryGetValue(union.FullName, out Dictionary<string, int> numbers))
                Reserve(sb, numbers.Where(n => !slots.Contains(n.Key)), null);
        }

        /***************************************************/

        private string ConsumerUnion(Type union)
        {
            (Type, string) key = (union, m_Current.Key);
            if (m_ConsumerUnions.TryGetValue(key, out string name))
                return name;

            name = Ascii(union.Name);
            if (m_Current.UsedNames.Contains(name) || m_AllTypes.Any(t => FileKey(t) == m_Current.Key && SimpleName(t) == name))
                name += "_Union";
            m_Current.UsedNames.Add(name);
            m_ConsumerUnions[key] = name;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"// Union of {union.FullName}, declared in {FileOf(union).Path}.");
            sb.AppendLine("// Defined in this file because all implementations live in this file and its dependencies, which the interface's own file cannot import.");
            sb.AppendLine($"message {name} {{");
            sb.AppendLine($"  option {MessageOption("csharp_type")} = {Quote(union.FullName)};");
            sb.AppendLine("  oneof value {");

            HashSet<string> slots = new HashSet<string>();
            foreach (Type subtype in Subtypes(union))
            {
                Enqueue(subtype);
                int number = FieldNumber(union, subtype.FullName, 1); // Same numbers wherever this interface appears.
                sb.AppendLine($"    {Reference(subtype)} {Snake(Name(subtype))} = {number};");
                slots.Add(subtype.FullName);
            }

            sb.AppendLine("  }");
            ReserveUnusedSlots(sb, union, slots);
            sb.AppendLine("}");
            m_Current.Definitions[union.FullName + "@consumer"] = sb.ToString();
            return name;
        }

        /***************************************************/

        private string EnumDefinition(Type type)
        {
            StringBuilder sb = new StringBuilder();
            Comment(sb, type.GetCustomAttribute<DescriptionAttribute>()?.Description, "");
            sb.AppendLine($"enum {Name(type)} {{");
            sb.AppendLine($"  option {EnumOption("csharp_enum")} = {Quote(type.FullName)};");

            string prefix = Snake(Name(type)).ToUpperInvariant();
            string[] names = Enum.GetNames(type);
            List<long> values = names.Select(n => System.Convert.ToInt64(Enum.Parse(type, n))).ToList();
            if (values.Distinct().Count() != values.Count)
                sb.AppendLine("  option allow_alias = true;  // The C# enum has duplicate values.");
            if (!values.Contains(0))
                sb.AppendLine($"  {prefix}_UNSPECIFIED = 0;  // proto3 requires a zero value.");
            for (int i = 0; i < names.Length; i++)
                sb.AppendLine($"  {prefix}_{Snake(names[i]).ToUpperInvariant()} = {values[i]};");

            sb.AppendLine("}");
            return sb.ToString();
        }

        /***************************************************/
        /**** Private Methods - Field types             ****/
        /***************************************************/

        private string FieldType(Type type, out string note)
        {
            note = null;
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                return "optional " + ElementType(nullable, ref note);

            if (type == typeof(string))
                return "string";

            if (type.FullName == ProtoTypes.FragmentSetName)
            {
                string element = ElementType(type.Assembly.GetType(ProtoTypes.FragmentInterfaceName), ref note);
                note = "FragmentSet" + (note != null ? "; " + note : "");
                return "repeated " + element;
            }

            if (ProtoTypes.IsDictionary(type, out Type keyType, out Type valueType))
            {
                if (valueType == typeof(object))
                    note = "values are BHoM messages, google.protobuf wrappers, or google.protobuf.Struct/ListValue";

                if (ProtoTypes.IsMapKey(keyType) && !ProtoTypes.IsCollection(valueType, out _))
                    return $"map<{ElementType(keyType, ref note)}, {ElementType(valueType, ref note)}>";

                return "repeated " + Wrapper(keyType, valueType);
            }

            if (ProtoTypes.IsCollection(type, out Type itemType))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
                    note = "unique values";

                if (ProtoTypes.IsCollection(itemType, out Type innerType))
                    return "repeated " + Wrapper(innerType, null);

                return "repeated " + ElementType(itemType, ref note);
            }

            return ElementType(type, ref note);
        }

        /***************************************************/

        private string ElementType(Type type, ref string note)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(bool)) return "bool";
            if (type == typeof(double) || type == typeof(decimal)) return "double";
            if (type == typeof(float)) return "float";
            if (type == typeof(int) || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte)) return "int32";
            if (type == typeof(long)) return "int64";
            if (type == typeof(uint) || type == typeof(ushort)) return "uint32";
            if (type == typeof(ulong)) return "uint64";
            if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)) return "string";
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return WellKnown("google.protobuf.Timestamp", "google/protobuf/timestamp.proto");
            if (type == typeof(TimeSpan)) return WellKnown("google.protobuf.Duration", "google/protobuf/duration.proto");
            if (type.FullName == "System.Drawing.Color") { note = "ARGB"; return "fixed32"; }
            if (type == typeof(Type)) { note = "C# type full name"; return "string"; }
            if (ProtoTypes.IsAnyValue(type))
            {
                if (type != typeof(object))
                    note = $"any {type.Name}: a message, or a google.protobuf wrapper for single values";
                return WellKnown("google.protobuf.Any", "google/protobuf/any.proto");
            }
            if (ProtoTypes.IsTuple(type)) return Tuple(type);

            if (typeof(Attribute).IsAssignableFrom(type))
                throw new SkipPropertyException(type);

            if (type.IsEnum && !ProtoTypes.IsOmType(type))
            {
                note = $"{type.FullName} value name";
                return "string";
            }

            if (ProtoTypes.IsOmType(type))
            {
                if (type.ContainsGenericParameters)
                {
                    note = "open generic type";
                    return WellKnown("google.protobuf.Any", "google/protobuf/any.proto");
                }

                if (IsOpenUnion(type))
                {
                    Resolve(type, UnionResolution.Any);
                    foreach (Type subtype in Subtypes(type))
                        Enqueue(subtype);
                    m_CurrentAnyOf = type.FullName;
                    return WellKnown("google.protobuf.Any", "google/protobuf/any.proto");
                }

                if (IsClosedUnion(type))
                {
                    if (IsCrossFileUnion(type))
                        Resolve(type, UnionResolution.Closed);
                    Enqueue(type);
                    return Reference(type);
                }

                if (ProtoTypes.IsUnion(type) && Subtypes(type).Count == 0)
                {
                    Resolve(type, UnionResolution.Any);
                    note = "no implementations in scope";
                    m_CurrentAnyOf = type.FullName;
                    return WellKnown("google.protobuf.Any", "google/protobuf/any.proto");
                }

                if (IsCrossFileUnion(type))
                {
                    if (m_Config.ConsumerSideUnions && CanDefineOnConsumerSide(type))
                    {
                        Resolve(type, UnionResolution.ConsumerSideUnion);
                        return ConsumerUnion(type);
                    }

                    if (m_Config.SkippedUnionTypes.Contains(type.FullName))
                    {
                        Resolve(type, UnionResolution.Skipped);
                        throw new SkipPropertyException(type);
                    }

                    if (IsAnyUnion(type))
                    {
                        Resolve(type, UnionResolution.Any);
                        foreach (Type subtype in Subtypes(type))
                            Enqueue(subtype);
                        m_CurrentAnyOf = type.FullName;
                        return WellKnown("google.protobuf.Any", "google/protobuf/any.proto");
                    }

                    Resolve(type, UnionResolution.TypedWithOther);
                }

                Enqueue(type);
                return Reference(type);
            }

            // .NET types without a protobuf equivalent, e.g. MethodBase or DataTable: the property is not exchanged.
            throw new SkipPropertyException(type);
        }

        /***************************************************/

        // Wrapper messages for nested collections ({1: repeated items}) and dictionaries with other keys ({1: key, 2: value}).
        private string Wrapper(Type first, Type second)
        {
            string note = null;
            if (second == null)
            {
                string inner = ElementType(first, ref note);
                string name = "ListOf" + WrapperPart(inner);
                m_Current.Wrappers[name] = $"message {name} {{\n  repeated {inner} items = 1;\n}}\n";
                return name;
            }
            else
            {
                string key = ElementType(first, ref note);
                string value = ProtoTypes.IsCollection(second, out Type item) ? "repeated " + ElementType(item, ref note) : ElementType(second, ref note);
                string name = $"{WrapperPart(key)}To{WrapperPart(value)}Entry";
                m_Current.Wrappers[name] = $"message {name} {{\n  {key} key = 1;\n  {value} value = 2;\n}}\n";
                return name;
            }
        }

        /***************************************************/

        // Wrapper message for System.Tuple: {1: Item1, 2: Item2, ...}.
        private string Tuple(Type type)
        {
            List<string> fields = new List<string>();
            List<string> parts = new List<string>();
            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                string fieldType = FieldType(arguments[i], out _);
                fields.Add($"  {fieldType} item{i + 1} = {i + 1};");
                parts.Add(WrapperPart(fieldType));
            }

            string name = "TupleOf" + string.Join("And", parts);
            m_Current.Wrappers[name] = $"message {name} {{\n{string.Join("\n", fields)}\n}}\n";
            return name;
        }

        /***************************************************/

        private static string WrapperPart(string protoType)
        {
            string last = protoType.Replace("repeated ", "ListOf").Split('.').Last();
            return string.Concat(last.Split('_').Select(s => s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s));
        }

        /***************************************************/
        /**** Private Methods - Helpers                 ****/
        /***************************************************/

        private static string QuantityText(PropertyInfo property)
        {
            foreach (object attribute in property.GetCustomAttributes(true))
            {
                Type attributeType = attribute.GetType();
                for (Type b = attributeType; b != null; b = b.BaseType)
                {
                    if (b.Name == "QuantityAttribute")
                    {
                        string siUnit = attributeType.GetProperty("SIUnit")?.GetValue(attribute) as string;
                        return $"[{attributeType.Name.Replace("Attribute", "")}{(string.IsNullOrEmpty(siUnit) ? "" : ", " + siUnit)}]";
                    }
                }
            }
            return "";
        }

        /***************************************************/

        private static void Comment(StringBuilder sb, string text, string indent)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (string line in text.Replace("\r", "").Split('\n'))
                sb.AppendLine($"{indent}// {line.Trim()}");
        }

        /***************************************************/

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        /***************************************************/

        // Strongly connected components with more than one file (Tarjan's algorithm).
        private static List<List<string>> FindCycles(Dictionary<string, List<string>> graph)
        {
            int index = 0;
            Dictionary<string, int> indices = new Dictionary<string, int>();
            Dictionary<string, int> lowLinks = new Dictionary<string, int>();
            HashSet<string> onStack = new HashSet<string>();
            Stack<string> stack = new Stack<string>();
            List<List<string>> result = new List<List<string>>();

            void Visit(string node)
            {
                indices[node] = lowLinks[node] = index++;
                stack.Push(node);
                onStack.Add(node);
                foreach (string next in graph.TryGetValue(node, out List<string> edges) ? edges : new List<string>())
                {
                    if (!indices.ContainsKey(next))
                    {
                        Visit(next);
                        lowLinks[node] = Math.Min(lowLinks[node], lowLinks[next]);
                    }
                    else if (onStack.Contains(next))
                    {
                        lowLinks[node] = Math.Min(lowLinks[node], indices[next]);
                    }
                }

                if (lowLinks[node] == indices[node])
                {
                    List<string> component = new List<string>();
                    string member;
                    do
                    {
                        member = stack.Pop();
                        onStack.Remove(member);
                        component.Add(member);
                    } while (member != node);

                    if (component.Count > 1)
                        result.Add(component.OrderBy(c => c, StringComparer.Ordinal).ToList());
                }
            }

            foreach (string node in graph.Keys)
            {
                if (!indices.ContainsKey(node))
                    Visit(node);
            }

            return result;
        }

        /***************************************************/
    }
}
