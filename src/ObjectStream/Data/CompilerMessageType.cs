using System;

namespace ObjectStream.Data
{
    [Serializable]
    public enum CompilerMessageType
    {
        Assembly,
        Compile,
        Error,
        Exit,
        Ready
    }
}
