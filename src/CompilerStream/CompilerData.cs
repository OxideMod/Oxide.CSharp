using System;
using System.Collections.Generic;

namespace Oxide.CSharp.CompilerStream
{
    [Serializable]
    public class CompilerData
    {
        public bool LoadDefaultReferences { get; set; }
        public string OutputFile { get; set; }
        public CompilerPlatform Platform { get; set; }
        public CompilerFile[] ReferenceFiles { get; set; }
        public string SdkVersion { get; set; }
        public List<CompilerFile> SourceFiles { get; set; }
        public bool StdLib { get; set; }
        public CompilerTarget Target { get; set; }
        public CompilerLanguageVersion Version { get; set; }
        public string Encoding { get; set; }
        public bool Debug { get; set; }
        public string[] Preprocessor { get; set; }

        public CompilerData()
        {
            StdLib = false;
            Target = CompilerTarget.Library;
            Platform = CompilerPlatform.AnyCPU;
            Version = CompilerLanguageVersion.V13;
            LoadDefaultReferences = false;
            SdkVersion = "2";
            Encoding = System.Text.Encoding.UTF8.WebName;
            Debug = false;
        }
    }
}
