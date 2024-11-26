using System;

namespace Oxide.CSharp.CompilerStream
{
    [Serializable]
    public class CompilationResult
    {
        public string Name { get; set; }
        public byte[] Data { get; set; }
        public byte[] Symbols { get; set; }

        public CompilationResult()
        {
            Data = new byte[0];
            Symbols = new byte[0];
        }
    }
}
