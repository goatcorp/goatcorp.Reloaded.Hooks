using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;
﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.Helpers;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Hooks.Tools;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Reloaded.Hooks.X64
{
    /// <summary>
    /// The <see cref="Wrapper"/> is a marshaller which converts a Microsoft x64 function call
    /// to Custom Calling Convention call.
    /// This means that you can call Custom Calling Convention functions as if it was a Microsoft x64 function.
    /// </summary>
    public static class Wrapper
    {
        /// <summary>
        /// Creates a wrapper function which allows you to call a function with a custom calling convention using the calling convention of
        /// <typeparamref name="TFunction"/>.
        /// </summary>
        /// <param name="functionAddress">Address of the function to wrap.</param>
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
        /// <param name="functionAddress">Address of the function to wrap.</param>
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
        /// <param name="functionAddress">Address of the function to wrap.</param>
        /// <param name="wrapperAddress">
        ///     Address of the wrapper used to call the original function.
        ///     If the original function uses the Microsoft calling convention, it is equal to the function address.
        /// </param>
        /// <returns>Address of the wrapper in native memory.</returns>
        public static nuint CreatePointer<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(Trimming.ReloadedAttributeTypes)]
#endif
        TFunction>(nuint functionAddress, out nuint wrapperAddress)
        {
            var attribute = FunctionAttribute.GetAttribute<TFunction>();
            wrapperAddress = functionAddress;

            // Hot path: Microsoft X64 functions require no wrapping.
            if (!attribute.Equals(FunctionAttribute.Microsoft))
                wrapperAddress = Create<TFunction>(functionAddress, attribute, FunctionAttribute.Microsoft);

            return wrapperAddress;
        }

        /// <summary>
        /// Creates a wrapper converting a call to a source calling convention to a given target calling convention.
        /// </summary>
        /// <param name="functionAddress">Address of the function in fromConvention to execute.</param>
        /// <param name="fromConvention">The calling convention to convert to toConvention. This is the convention of the function (<paramref name="functionAddress"/>) called.</param>
        /// <param name="toConvention">The target convention to which convert to fromConvention. This is the convention of the function returned.</param>
        /// <returns>Address of the wrapper in memory you can call .</returns>
        public static nuint Create<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(Trimming.ReloadedAttributeTypes)]
#endif
        TFunction>(nuint functionAddress, IFunctionAttribute fromConvention, IFunctionAttribute toConvention)
        {
            // 384 Bytes should allow for around 100 parameters in worst case scenario.
            // If you need more than that, then... I don't know what you're doing with your life.
            // Please do a pull request though and we can stick some code to predict the size.
            const int MaxFunctionSize = 384;
            var assembler = new Assembler(bitness: 64);
            var minMax = Utilities.GetRelativeJumpMinMax(functionAddress, Int32.MaxValue - MaxFunctionSize);
            var buffer = Utilities.FindOrCreateBufferInRange(MaxFunctionSize, minMax.min, minMax.max);
            int numberOfParameters = Utilities.GetNumberofParametersWithoutFloats<TFunction>();

            return buffer.ExecuteWithLock(() =>
            {
                // Align the code.
                buffer.SetAlignment(16);
                var codeAddress = buffer.Properties.WritePointer;

                // On enter, stack is misaligned by 8.
                // Callee Backup Registers
                // Backup Stack Frame
                assembler.push(rbp);       // Backup old call frame
                assembler.mov(rbp, rsp);   // Setup new call frame
                foreach (var register in toConvention.CalleeSavedRegisters)
                    assembler.push(GetRegister64(register));

                // Even my mind gets a bit confused. So here is a reminder:
                // fromConvention is the convention that gets called.
                // toConvention is the convention we are marshalling from.
                int targetStackParameters = numberOfParameters - fromConvention.SourceRegisters.Length;
                targetStackParameters = targetStackParameters < 0 ? 0 : targetStackParameters;

                int stackParamBytesTotal = ((targetStackParameters) * 8);
                int stackMisalignment = (stackParamBytesTotal + 8) % 16; // Adding +8 here because we assume that we are starting with a misaligned stack.
                int shadowSpace = fromConvention.ShadowSpace ? 32 : 0;

                // Note: Our stack is already aligned at this point because of `push` above.
                // stackBytesTotal % 16 represent the amount of bytes away from alignment after pushing parameters up the stack.
                // Setup stack alignment.
                if (stackMisalignment != 0)
                    assembler.sub(rsp, stackMisalignment);

                // Setup parameters for target.
                if (numberOfParameters > 0)
                    AssembleFunctionParameters(assembler, numberOfParameters, ref fromConvention, ref toConvention);

                // Make shadow space if target requires it.
                if (fromConvention.ShadowSpace)
                    assembler.sub(rsp, shadowSpace);

                // Call target.
                assembler.call(functionAddress);

                // Restore the stack pointer after function call.
                if (stackParamBytesTotal + shadowSpace + stackMisalignment != 0)
                    assembler.add(rsp, stackParamBytesTotal + shadowSpace + stackMisalignment);

                // Marshal return register back from target to source.
                if (fromConvention.ReturnRegister != toConvention.ReturnRegister)
                    assembler.mov(GetRegister64(toConvention.ReturnRegister), GetRegister64(fromConvention.ReturnRegister));

                // Callee Restore Registers
                foreach (var register in toConvention.CalleeSavedRegisters.AsEnumerable().Reverse())
                    assembler.pop(GetRegister64(register));

                assembler.pop(rbp);
                assembler.ret();

                // Write function to buffer and return pointer.
                using var stream = new MemoryStream();
                var result = assembler.Assemble(new StreamCodeWriter(stream), codeAddress);

                return buffer.Add(stream.ToArray(), 1);
            });
        }

        private static void AssembleFunctionParameters(Assembler assembler, int parameterCount, ref IFunctionAttribute fromConvention, ref IFunctionAttribute toConvention)
        {
            /*
               At the current moment in time, our register contents and parameters are as follows: RCX, RDX, R8, R9.
               The base address of old call stack (RBP) is at [rbp + 0]
               The return address of the calling function is at [rbp + 8]
               Last stack parameter is at [rbp + 16] (+ "Shadow Space").

               Note: Reason return address is not at [rbp + 0] is because we pushed rbp and mov'd rsp to it.
               Reminder: The stack grows by DECREMENTING THE STACK POINTER.

               Example (Parameters passed right to left)
               Stack 1st Param: [rbp + 16]
               Stack 2nd Param: [rbp + 24]

               Parameter Count == 1:
               baseStackOffset = 8
               lastParameter = baseStackOffset + (toStackParams * 8) == 16
            */

            int toStackParams     = parameterCount - toConvention.SourceRegisters.Length;
            int baseStackOffset   = (toConvention.ShadowSpace ? 32 : 0) + 8; // + 8
            baseStackOffset      += (toStackParams) * 8;

            // Re-push all toConvention stack params, then register parameters. (Right to Left)
            for (int x = 0; x < toStackParams; x++)
            {
                assembler.push(__qword_ptr[rbp + baseStackOffset]);
                baseStackOffset -= 8;
            }
            
            for (int x = Math.Min(toConvention.SourceRegisters.Length, parameterCount) - 1; x >= 0; x--) 
                assembler.push(GetRegister64(toConvention.SourceRegisters[x]));

            // Pop all necessary registers to target. (Left to Right)
            for (int x = 0; x < fromConvention.SourceRegisters.Length && x < parameterCount; x++)
                assembler.pop(GetRegister64(fromConvention.SourceRegisters[x]));
        }

        private static AssemblerRegister64 GetRegister64(object reg)
        {
            string name = reg.ToString().ToLower();
            var field = typeof(AssemblerRegisters).GetField(name);
            return (AssemblerRegister64)field.GetValue(null);
        }
    }
}
