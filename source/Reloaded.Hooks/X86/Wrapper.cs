using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.Helpers;
using Reloaded.Hooks.Definitions.Internal;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Hooks.Internal;
using Reloaded.Hooks.Tools;
using static Reloaded.Hooks.Definitions.X86.FunctionAttribute;

using Reloaded.Hooks.Definitions.Structs;
using System.Diagnostics.CodeAnalysis;

namespace Reloaded.Hooks.X86
{
    /// <summary>
    /// Allows for creating wrapper functions allow you to call functions with custom calling conventions using the calling convention of a given delegate.
    /// </summary>
    public static class Wrapper
    {
        /// <summary>
        /// Creates a wrapper function which allows you to call a function with a custom calling convention using the calling convention of
        /// using the calling convention of <typeparamref name="TFunction"/>.
        /// </summary>
        /// <param name="functionAddress">Address of the function to reverse wrap..</param>
        public static TFunction Create<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(Trimming.ReloadedAttributeTypes)]
#endif
        TFunction>(nuint functionAddress)
        {
            return Create<TFunction>(functionAddress, out var wrapperAddress);
        }

        /// <summary>
        /// Creates a wrapper function which allows you to call a function with a custom calling convention using the calling convention of
        /// <typeparamref name="TFunction"/>.
        /// </summary>
        /// <param name="functionAddress">Address of the function to reverse wrap..</param>
        /// <param name="wrapperAddress">
        ///     Address of the wrapper used to call the original function.
        ///     If the source and target calling conventions match, this is the same as <paramref name="functionAddress"/>
        /// </param>
        public static TFunction Create<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(Trimming.ReloadedAttributeTypes)]
#endif
        TFunction>(nuint functionAddress, out nuint wrapperAddress)
        {
            CreatePointer<TFunction>(functionAddress, out wrapperAddress);
            if (typeof(TFunction).IsValueType && !typeof(TFunction).IsPrimitive)
                return Unsafe.As<nuint, TFunction>(ref wrapperAddress);

            return Marshal.GetDelegateForFunctionPointer<TFunction>(wrapperAddress.ToSigned());
        }

        /// <summary>
        /// Creates a wrapper function which allows you to call a function with a custom calling convention using the calling convention of
        /// <typeparamref name="TFunction"/>.
        /// </summary>
        /// <param name="functionAddress">Address of the function to reverse wrap..</param>
        /// <param name="wrapperAddress">
        ///     Address of the wrapper used to call the original function.
        ///     If the original function is CDECL, the wrapper address equals the function address.
        /// </param>
        /// <returns>Address of the wrapper in native memory.</returns>
        public static nuint CreatePointer<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(Trimming.ReloadedAttributeTypes)]
#endif
        TFunction>(nuint functionAddress, out nuint wrapperAddress)
        {
            var attribute = GetAttribute<TFunction>();
            wrapperAddress = functionAddress;

            // Hot path: Don't create wrapper if both conventions are already compatible.
            var funcPtrAttribute = Misc.TryGetAttributeOrDefault<TFunction, UnmanagedFunctionPointerAttribute>();
            if (!attribute.IsEquivalent(funcPtrAttribute))
                wrapperAddress = Create<TFunction>(functionAddress, attribute, attribute.GetEquivalent(funcPtrAttribute));

            return wrapperAddress;
        }

        /// <summary>
        /// Creates a wrapper function which allows you to call a function with a custom calling convention using the calling convention of
        /// <typeparamref name="TFunction"/>.
        /// </summary>
        /// <param name="functionAddress">The address of the function using <paramref name="fromConvention"/>.</param>
        /// <param name="fromConvention">The calling convention to convert to <paramref name="toConvention"/>. This is the convention of the function (<paramref name="functionAddress"/>) called.</param>
        /// <param name="toConvention">The target convention to which convert to <paramref name="fromConvention"/>. This is the convention of the function returned.</param>
        /// <returns>Address of the wrapper in memory.</returns>
        public static nuint Create<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(Trimming.ReloadedAttributeTypes)]
#endif
        TFunction>(nuint functionAddress, IFunctionAttribute fromConvention, IFunctionAttribute toConvention)
        {
            throw new NotImplementedException();
        }

        private static string[] AssembleFunctionParameters(int parameterCount, Register[] fromRegisters, Register[] toRegisters)
        {
            List<string> assemblyCode = new List<string>();

            /*
               At the current moment in time,
               The base address of old call stack (EBP) is at [ebp + 0]
               The return address of the calling function is at [ebp + 4]
               Last parameter is therefore at [ebp + 8].

               Note: Reason return address is not at [ebp + 0] is because we pushed ebp and mov'd esp to it.
               Reminder: The stack grows by DECREMENTING THE STACK POINTER.
            
               Note 2: Don't need to account for reserved stack space in toConvention because we reserve it before pushing the parameters.
             */

            // The initial offset from EBP (Stack Base Pointer) for the rightmost parameter (right to left passing):
            int toStackParams   = parameterCount - toRegisters.Length;          // Stack parameter count in toConvention
            int baseStackOffset = ((toStackParams) * 4) + 4;                    // + 4 because base address of old call stack is currently at ebp + 0

            // Re-push all toConvention stack params, then register parameters. (Right to Left)
            for (int x = 0; x < toStackParams; x++)
            {
                assemblyCode.Add($"push dword [ebp + {baseStackOffset}]");
                baseStackOffset -= 4;
            }

            for (int x = Math.Min(toRegisters.Length, parameterCount) - 1; x >= 0; x--)
                assemblyCode.Add($"push {toRegisters[x]}");

            // Now pop all necessary registers to target. (Left to Right)
            for (int x = 0; x < fromRegisters.Length && x < parameterCount; x++)
                assemblyCode.Add($"pop {fromRegisters[x]}");

            return assemblyCode.ToArray();
        }
    }
}
