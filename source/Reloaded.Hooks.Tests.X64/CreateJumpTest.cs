using System;
using Iced.Intel;
using Reloaded.Hooks.Tools;
using Xunit;

namespace Reloaded.Hooks.Tests.X64;

/// <summary>
/// Tests the jump stubs created for reverse wrappers.
/// </summary>
public unsafe class CreateJumpTest
{
    private const int TargetOffset = 8;

    [Fact]
    public void JumpsThroughAlignedTargetInStub()
    {
        var target = unchecked((nuint)0x1234_5678_9ABCUL);

        var stub = Utilities.CreateJump(target, true);
        var jump = DecodeAt(stub);

        Assert.Equal(0ul, (ulong)stub % 16);
        Assert.Equal(Mnemonic.Jmp, jump.Mnemonic);
        Assert.Equal(OpKind.Memory, jump.Op0Kind);
        Assert.Equal(Register.RIP, jump.MemoryBase);
        Assert.Equal((ulong)(stub + TargetOffset), jump.IPRelativeMemoryAddress);
        Assert.Equal((ulong)target, *(ulong*)(stub + TargetOffset));
    }

    [Fact]
    public void JumpsToTargetsWithNoFreeMemoryNearby()
    {
        // Everything within rel32 range of this lies above the user mode address space.
        var target = unchecked((nuint)0x9000_0000_0000UL);

        var stub = Utilities.CreateJump(target, true);

        Assert.Equal((ulong)target, *(ulong*)(stub + TargetOffset));
    }

    private static Instruction DecodeAt(nuint address)
    {
        var bytes = new ReadOnlySpan<byte>((void*)address, 16).ToArray();
        return Decoder.Create(64, new ByteArrayCodeReader(bytes), address).Decode();
    }
}
