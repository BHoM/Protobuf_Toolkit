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
using System.Linq;
using System.Reflection;

namespace BH.Engine.Adapters.gRPC
{
    // Type classification shared by the generator, the writer and the reader, so all three agree on how a C# type is encoded.
    internal static class ProtoTypes
    {
        /***************************************************/
        /**** Constants                                 ****/
        /***************************************************/

        // Field number of the google.protobuf.Any slot in a union, used for implementations without a typed slot.
        internal const int OtherFieldNumber = 50000;

        internal const string TypeUrlPrefix = "type.googleapis.com/";

        internal const string FragmentSetName = "BH.oM.Base.FragmentSet";

        internal const string FragmentInterfaceName = "BH.oM.Base.IFragment";

        internal static readonly string[] BaseProperties = { "BHoM_Guid", "Name", "Fragments", "Tags", "CustomData" };

        /***************************************************/
        /**** Internal Methods                          ****/
        /***************************************************/

        // Properties in field number order: the BHoMObject properties first, then declaration order.
        internal static List<PropertyInfo> Properties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetGetMethod() != null)
                .OrderBy(p => BaseIndex(p.Name) >= 0 ? BaseIndex(p.Name) : 100)
                .ThenBy(p => p.MetadataToken)
                .ToList();
        }

        /***************************************************/

        // Properties by name. A property hidden with 'new' in a subclass appears once, as in the generated message.
        internal static Dictionary<string, PropertyInfo> PropertiesByName(Type type)
        {
            return m_PropertiesByName.GetOrAdd(type, t => Properties(t)
                .GroupBy(p => p.Name)
                .ToDictionary(g => g.Key, g => g.First()));
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> m_PropertiesByName =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();

        /***************************************************/

        internal static int BaseIndex(string propertyName)
        {
            return Array.IndexOf(BaseProperties, propertyName);
        }

        /***************************************************/

        internal static bool IsOmType(Type type)
        {
            return type.Namespace != null && type.Namespace.StartsWith("BH.oM");
        }

        /***************************************************/

        // Interfaces and abstract classes of the oM become unions (oneof) over their implementations.
        internal static bool IsUnion(Type type)
        {
            return IsOmType(type) && (type.IsInterface || type.IsAbstract) && !(type.IsAbstract && type.IsSealed);
        }

        /***************************************************/

        internal static bool IsDictionary(Type type, out Type keyType, out Type valueType)
        {
            keyType = valueType = typeof(object);
            Type dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

            if (dictionary == null)
                return false;

            keyType = dictionary.GetGenericArguments()[0];
            valueType = dictionary.GetGenericArguments()[1];
            return true;
        }

        /***************************************************/

        internal static bool IsCollection(Type type, out Type itemType)
        {
            itemType = typeof(object);
            if (type == typeof(string))
                return false;

            if (type.IsArray)
            {
                // A two dimensional array is a collection of rows.
                itemType = type.GetArrayRank() == 2 ? type.GetElementType().MakeArrayType() : type.GetElementType();
                return true;
            }

            Type enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumerable == null)
                return false;

            itemType = enumerable.GetGenericArguments()[0];
            return true;
        }

        /***************************************************/

        // Dictionary keys that protobuf accepts in a map<,>. Other keys use a repeated key/value message.
        internal static bool IsMapKey(Type type)
        {
            return type == typeof(string) || type == typeof(int) || type == typeof(long) || type == typeof(uint) || type == typeof(ulong) || type == typeof(bool);
        }

        /***************************************************/

        // Scalars encoded as varint, fixed32 or fixed64. Only these can be packed in a repeated field.
        internal static bool IsPackable(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(double) || type == typeof(float) || type == typeof(decimal) || type == typeof(int) || type == typeof(long)
                || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(uint) || type == typeof(ushort)
                || type == typeof(ulong) || type == typeof(bool) || (type.IsEnum && IsOmType(type)) || type.FullName == "System.Drawing.Color";
        }

        /***************************************************/

        // Values that can hold anything: object and non-oM interfaces such as IComparable. Encoded as google.protobuf.Any.
        internal static bool IsAnyValue(Type type)
        {
            return type == typeof(object) || (type.IsInterface && !IsOmType(type));
        }

        /***************************************************/

        internal static bool IsTuple(Type type)
        {
            return type.IsGenericType && type.FullName != null && type.FullName.StartsWith("System.Tuple`");
        }

        /***************************************************/

        internal static bool IsTwoDimensionalArray(Type type)
        {
            return type.IsArray && type.GetArrayRank() == 2;
        }

        /***************************************************/

        // Resolves a type from its full name, including closed generic types, across the loaded assemblies.
        internal static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            Type type = Type.GetType(fullName, false);
            if (type != null)
                return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Dynamic or partially loaded assemblies can throw: ignore them.
                }
            }

            return null;
        }

        /***************************************************/
    }
}
