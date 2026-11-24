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
using System.ComponentModel;
using System.IO;
using System.Text;
using Google.Protobuf;

namespace BH.Engine.Adapters.Protobuf.Objects
{
    [Description("Reads the protobuf wire format from a byte array, with limits so nested messages are read in place. Google.Protobuf's CodedInputStream does the same, but keeps its PushLimit / PopLimit internal. The values are decoded as CodedInputStream decodes them.")]
    public sealed class ProtoReader
    {
        /***************************************************/
        /**** Constructors                              ****/
        /***************************************************/

        public ProtoReader(byte[] buffer)
        {
            m_Buffer = buffer ?? new byte[0];
            m_Limit = m_Buffer.Length;
        }

        /***************************************************/
        /**** Tags and limits                           ****/
        /***************************************************/

        // True at the end of the buffer or of the current limit.
        public bool IsAtEnd => m_Position >= m_Limit;

        // The next tag, or 0 at the end of the buffer or of the current limit.
        public uint ReadTag()
        {
            if (m_Position >= m_Limit)
            {
                m_LastTag = 0;
                return 0;
            }

            m_LastTag = ReadRawVarint32();
            if (WireFormat.GetTagFieldNumber(m_LastTag) == 0)
                throw new InvalidDataException("Invalid protobuf tag: field number 0.");
            return m_LastTag;
        }

        public int ReadLength()
        {
            int length = (int)ReadRawVarint32();
            if (length < 0)
                throw new InvalidDataException("Invalid protobuf length.");
            return length;
        }

        // Limits reading to the next length bytes. Returns the previous limit, to pass to PopLimit.
        public int PushLimit(int length)
        {
            int previous = m_Limit;
            int limit = m_Position + length;
            if (length < 0 || limit > previous)
                throw new InvalidDataException("A protobuf length goes beyond the end of its message.");
            m_Limit = limit;
            return previous;
        }

        // Restores the previous limit, after reading up to the current one.
        public void PopLimit(int previous)
        {
            m_Position = m_Limit;
            m_Limit = previous;
        }

        /***************************************************/
        /**** Values                                    ****/
        /***************************************************/

        public double ReadDouble() => BitConverter.Int64BitsToDouble((long)ReadRawLittleEndian64());

        public float ReadFloat()
        {
            uint bits = ReadRawLittleEndian32();
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        public uint ReadFixed32() => ReadRawLittleEndian32();

        public ulong ReadFixed64() => ReadRawLittleEndian64();

        public int ReadInt32() => (int)ReadRawVarint64();

        public long ReadInt64() => (long)ReadRawVarint64();

        public uint ReadUInt32() => (uint)ReadRawVarint64();

        public ulong ReadUInt64() => ReadRawVarint64();

        public bool ReadBool() => ReadRawVarint64() != 0;

        public int ReadEnum() => (int)ReadRawVarint64();

        public string ReadString()
        {
            int length = ReadLength();
            Require(length);
            string text = length == 0 ? "" : Encoding.UTF8.GetString(m_Buffer, m_Position, length);
            m_Position += length;
            return text;
        }

        public byte[] ReadBytes()
        {
            int length = ReadLength();
            Require(length);
            byte[] bytes = new byte[length];
            Buffer.BlockCopy(m_Buffer, m_Position, bytes, 0, length);
            m_Position += length;
            return bytes;
        }

        /***************************************************/

        // Skips the field of the last tag read.
        public void SkipLastField()
        {
            switch (WireFormat.GetTagWireType(m_LastTag))
            {
                case WireFormat.WireType.Varint:
                    ReadRawVarint64();
                    break;
                case WireFormat.WireType.Fixed64:
                    Skip(8);
                    break;
                case WireFormat.WireType.Fixed32:
                    Skip(4);
                    break;
                case WireFormat.WireType.LengthDelimited:
                    Skip(ReadLength());
                    break;
                case WireFormat.WireType.StartGroup:
                    // Deprecated groups: skip every field up to the matching end group.
                    int number = WireFormat.GetTagFieldNumber(m_LastTag);
                    uint tag;
                    while ((tag = ReadTag()) != 0)
                    {
                        if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.EndGroup)
                        {
                            if (WireFormat.GetTagFieldNumber(tag) != number)
                                throw new InvalidDataException("Mismatched protobuf end group.");
                            return;
                        }
                        SkipLastField();
                    }
                    throw new InvalidDataException("Truncated protobuf group.");
                case WireFormat.WireType.EndGroup:
                    break;
                default:
                    throw new InvalidDataException("Invalid protobuf wire type.");
            }
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private uint ReadRawVarint32()
        {
            // Varints of 32 bit values may be written with up to 10 bytes (negative int32): read them as 64 bits.
            return (uint)ReadRawVarint64();
        }

        private ulong ReadRawVarint64()
        {
            ulong result = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                Require(1);
                byte b = m_Buffer[m_Position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            throw new InvalidDataException("Malformed protobuf varint.");
        }

        private uint ReadRawLittleEndian32()
        {
            Require(4);
            uint value = m_Buffer[m_Position] | (uint)m_Buffer[m_Position + 1] << 8 | (uint)m_Buffer[m_Position + 2] << 16 | (uint)m_Buffer[m_Position + 3] << 24;
            m_Position += 4;
            return value;
        }

        private ulong ReadRawLittleEndian64()
        {
            ulong low = ReadRawLittleEndian32();
            ulong high = ReadRawLittleEndian32();
            return low | high << 32;
        }

        private void Skip(int count)
        {
            Require(count);
            m_Position += count;
        }

        private void Require(int count)
        {
            if (count < 0 || m_Position + count > m_Limit)
                throw new InvalidDataException("Truncated protobuf message.");
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private readonly byte[] m_Buffer;

        private int m_Position = 0;

        private int m_Limit;

        private uint m_LastTag = 0;

        /***************************************************/
    }
}
