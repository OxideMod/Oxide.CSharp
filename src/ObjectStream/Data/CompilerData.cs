﻿using System;

namespace ObjectStream.Data
{
    [Serializable]
    public class CompilerData
    {
        public CompilerData()
        {
            StdLib = false;
            Target = CompilerTarget.Library;
            Platform = CompilerPlatform.AnyCPU;
            Version = CompilerLanguageVersion.Preview;
            LoadDefaultReferences = false;
            SdkVersion = "2";
            Encoding = System.Text.Encoding.Default.WebName;
            Debug = false;
        }

        public bool LoadDefaultReferences { get; set; }
        public string OutputFile { get; set; }
        public CompilerPlatform Platform { get; set; }
        public CompilerFile[] ReferenceFiles { get; set; }
        public string SdkVersion { get; set; }
        public CompilerFile[] SourceFiles { get; set; }
        public bool StdLib { get; set; }
        public CompilerTarget Target { get; set; }
        public CompilerLanguageVersion Version { get; set; }
        public string Encoding { get; set; }
        public bool Debug { get; set; }
    }
}
