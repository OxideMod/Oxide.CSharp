﻿using System;

namespace ObjectStream.Data
{
    [Serializable]
    public class CompilerMessage
    {
        public object Data { get; set; }

        public object ExtraData { get; set; }

        public int Id { get; set; }

        public CompilerMessageType Type { get; set; }
    }
}
