using System;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Tests.Shared;
using Xunit;

namespace Reloaded.Hooks.Tests.X64;

/// <summary>
/// Tests that hooks are enabled and disabled by retargeting the jump in their reverse wrapper.
/// </summary>
public unsafe class HookToggleTest : IDisposable
{
    private const int TargetOffset = 8;

    /// <summary>
    /// <see cref="FastcallCalculator.Add"/> takes its parameters in the same registers as the Microsoft convention,
    /// so hooking it with this needs no wrapper converting between conventions.
    /// </summary>
    public delegate int MicrosoftAddFunction(int a, int b);

    private FastcallCalculator _calculator = new FastcallCalculator();
    private IHook<MicrosoftAddFunction> _microsoftHook;
    private IHook<FastcallCalculator.AddFunction> _fastcallHook;

    public void Dispose() => _calculator.Dispose();

    [Fact]
    public void TogglingOnlyRetargetsReverseWrapper()
    {
        int Hookfunction(int a, int b) => _microsoftHook.OriginalFunction(a, b) + 1;
        _microsoftHook = ReloadedHooks.Instance.CreateHook<MicrosoftAddFunction>(Hookfunction, (long)_calculator.Add).Activate();
        var add = ReloadedHooks.Instance.CreateWrapper<MicrosoftAddFunction>((long)_calculator.Add, out _);

        var stub = (byte*)_microsoftHook.ReverseWrapper.WrapperPointer;
        var code = new ReadOnlySpan<byte>(stub, TargetOffset).ToArray();
        var target = (nint*)(stub + TargetOffset);
        var enabledTarget = *target;
        Assert.Equal(_microsoftHook.ReverseWrapper.NativeFunctionPtr, enabledTarget);
        Assert.Equal(3, add(1, 1));

        _microsoftHook.Disable();
        Assert.Equal(_microsoftHook.OriginalFunctionAddress, *target);
        Assert.Equal(2, add(1, 1));

        _microsoftHook.Enable();
        Assert.Equal(enabledTarget, *target);
        Assert.Equal(3, add(1, 1));

        Assert.Equal(code, new ReadOnlySpan<byte>(stub, TargetOffset).ToArray());
    }

    [Fact]
    public void TogglesHooksWithConventionWrappers()
    {
        int Hookfunction(int a, int b) => _fastcallHook.OriginalFunction(a, b) + 1;
        _fastcallHook = ReloadedHooks.Instance.CreateHook<FastcallCalculator.AddFunction>(Hookfunction, (long)_calculator.Add).Activate();
        var add = ReloadedHooks.Instance.CreateWrapper<FastcallCalculator.AddFunction>((long)_calculator.Add, out _);

        // Reverse wrapper should lead to a wrapper converting the convention, rather than straight to the delegate.
        var target = (nint*)(_fastcallHook.ReverseWrapper.WrapperPointer + TargetOffset);
        Assert.NotEqual(_fastcallHook.ReverseWrapper.NativeFunctionPtr, *target);
        Assert.Equal(3, add(1, 1));

        _fastcallHook.Disable();
        Assert.Equal(2, add(1, 1));

        _fastcallHook.Enable();
        Assert.Equal(3, add(1, 1));
    }
}
