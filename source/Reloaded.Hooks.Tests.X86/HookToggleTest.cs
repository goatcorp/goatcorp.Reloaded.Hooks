using System;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Hooks.Tests.Shared;
using Xunit;

namespace Reloaded.Hooks.Tests.X86;

/// <summary>
/// Tests that hooks are enabled and disabled by retargeting the jump in their reverse wrapper.
/// </summary>
public unsafe class HookToggleTest : IDisposable
{
    private const int TargetOffset = 8;

    /// <summary>
    /// Unlike <see cref="NativeCalculator.AddFunction"/>, this is also marshalled as Cdecl,
    /// so hooking <see cref="NativeCalculator.Add"/> with it needs no wrapper converting between conventions.
    /// </summary>
    [Function(CallingConventions.Cdecl)]
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int CdeclAddFunction(int a, int b);

    private NativeCalculator _calculator = new NativeCalculator();
    private IHook<CdeclAddFunction> _hook;

    public void Dispose() => _calculator.Dispose();

    [Fact]
    public void TogglingOnlyRetargetsReverseWrapper()
    {
        int Hookfunction(int a, int b) => _hook.OriginalFunction(a, b) + 1;
        _hook = ReloadedHooks.Instance.CreateHook<CdeclAddFunction>(Hookfunction, (long)_calculator.Add).Activate();
        var add = ReloadedHooks.Instance.CreateWrapper<CdeclAddFunction>((long)_calculator.Add, out _);

        var stub = (byte*)_hook.ReverseWrapper.WrapperPointer;
        var code = new ReadOnlySpan<byte>(stub, TargetOffset).ToArray();
        var target = (nint*)(stub + TargetOffset);
        var enabledTarget = *target;
        Assert.Equal(_hook.ReverseWrapper.NativeFunctionPtr, enabledTarget);
        Assert.Equal(3, add(1, 1));

        _hook.Disable();
        Assert.Equal(_hook.OriginalFunctionAddress, *target);
        Assert.Equal(2, add(1, 1));

        _hook.Enable();
        Assert.Equal(enabledTarget, *target);
        Assert.Equal(3, add(1, 1));

        Assert.Equal(code, new ReadOnlySpan<byte>(stub, TargetOffset).ToArray());
    }
}
