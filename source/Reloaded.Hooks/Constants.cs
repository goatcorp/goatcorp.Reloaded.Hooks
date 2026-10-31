namespace Reloaded.Hooks
{
    internal class Constants
    {
        /// <summary>
        /// Maximum size of a jmp instruction. [Mnemonic: jmp qword [qword 0xFFFFFFFF]]
        /// </summary>
        internal const int MaxAbsJmpSize = 8;

        /// <summary>
        /// Size of the stubs created by <see cref="Tools.Utilities.CreateJump"/>.
        /// </summary>
        internal const int JumpStubSize = 16;

        /// <summary>
        /// Offset of the address the stubs created by <see cref="Tools.Utilities.CreateJump"/> jump to.
        /// </summary>
        internal const int JumpStubTargetOffset = 8;
    }
}
