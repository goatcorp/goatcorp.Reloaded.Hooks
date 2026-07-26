using System;
using Reloaded.Memory.Buffers;

namespace Reloaded.Hooks.Internal
{
    internal static class MemoryBufferExtensions
    {
        internal static nuint AddOrThrow(this MemoryBuffer buffer, byte[] bytesToWrite, int alignment = 4)
        {
            var address = buffer.Add(bytesToWrite, alignment);
            if (address == 0)
                throw new OutOfMemoryException(Describe(buffer, bytesToWrite.Length.ToString()));

            return address;
        }
        
        internal static nuint AddOrThrow(this MemoryBuffer buffer, int numBytes, int alignment = 4)
        {
            var address = buffer.Add(numBytes, alignment);
            if (address == 0)
                throw new OutOfMemoryException(Describe(buffer, numBytes.ToString()));

            return address;
        }
        
        internal static nuint AddOrThrow<TStructure>(this MemoryBuffer buffer, ref TStructure item, bool marshalElement = false, int alignment = 4)
        {
            var address = buffer.Add(ref item, marshalElement, alignment);
            if (address == 0)
                throw new OutOfMemoryException(Describe(buffer, typeof(TStructure).Name));

            return address;
        }
        
        internal static nuint AddAtOrThrow(this MemoryBuffer buffer, nuint expectedAddress, byte[] bytesToWrite, int alignment = 4)
        {
            var address = buffer.AddOrThrow(bytesToWrite, alignment);
            if (address != expectedAddress)
                throw new InvalidOperationException(
                    $"Data needed to land at 0x{(ulong)expectedAddress:X} but landed at 0x{(ulong)address:X} instead. " +
                    $"Something appended to this buffer between its write pointer being read and the data being written.");

            return address;
        }

        private static string Describe(MemoryBuffer buffer, string required)
        {
            var properties = buffer.Properties;
            return $"MemoryBuffer at 0x{(ulong)properties.DataPointer:X} could not fit {required}. " +
                   $"{properties.Remaining} of {properties.Size} bytes remaining.";
        }
    }
}
