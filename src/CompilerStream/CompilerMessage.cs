using System;

namespace Oxide.CSharp.CompilerStream
{
    [Serializable]
    public sealed class CompilerMessage
    {
        public int Id { get; set; }

        public MessageType Type { get; set; }

        public byte[] Data { get; set; }
    }
}
