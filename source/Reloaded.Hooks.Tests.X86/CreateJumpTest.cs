using System;
using Iced.Intel;
using Reloaded.Hooks.Tools;
using Xunit;

namespace Reloaded.Hooks.Tests.X86;

/// <summary>
/// Tests the jump stubs created for reverse wrappers.
/// </summary>
public unsafe class CreateJumpTest
{
    private const int TargetOffset = 8;

    [Fact]
    public void JumpsThroughAlignedTargetInStub()
    {
        var target = (nuint)0x1234_5678;

        var stub = Utilities.CreateJump(target, false);
        var jump = DecodeAt(stub);

        Assert.Equal(0ul, (ulong)stub % 16);
        Assert.Equal(Mnemonic.Jmp, jump.Mnemonic);
        Assert.Equal(OpKind.Memory, jump.Op0Kind);
        Assert.Equal(Register.None, jump.MemoryBase);
        Assert.Equal((uint)(stub + TargetOffset), jump.MemoryDisplacement32);
        Assert.Equal((uint)target, *(uint*)(stub + TargetOffset));
    }

    private static Instruction DecodeAt(nuint address)
    {
        var bytes = new ReadOnlySpan<byte>((void*)address, 16).ToArray();
        return Decoder.Create(32, new ByteArrayCodeReader(bytes), address).Decode();
    }
}
