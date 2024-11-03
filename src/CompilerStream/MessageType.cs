using System;

namespace Oxide.CSharp.CompilerStream
{
    [Flags]
    [Serializable]
    public enum MessageType : byte
    {
        Unknown = 0x00,

        Acknowledge = 0x01,

        Heartbeat = 0x02,

        VersionInfo = 0x04,

        Ready = 0x08,

        Command = 0x10,

        Data = 0x20,

        Error = 0x40,

        Shutdown = 80
    }
}
