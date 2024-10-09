using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Oxide.CSharp.CompilerStream
{
    public sealed class CompilerMessage
    {
        public MessageType Type { get; set; }

        public byte[] Data { get; set; }
    }
}
