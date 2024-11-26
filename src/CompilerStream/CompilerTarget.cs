using System;

namespace Oxide.CSharp.CompilerStream
{
    [Serializable]
    public enum CompilerTarget
    {
        Library,
        Exe,
        Module,
        WinExe
    }
}
