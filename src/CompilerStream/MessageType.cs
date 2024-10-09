using System;

namespace Oxide.CSharp.CompilerStream
{
    [Flags]
    public enum MessageType : byte
    {
        Unknown = 0x00,

        Acknowledge = 0x01,

        Heartbeat = 0x02,

        VersionInfo = 0x04,

        Ready = 0x08,

        Command = 0x16,

        Data = 0x32,

        Shutdown = 0x64
    }
}
