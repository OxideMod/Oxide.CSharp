using System;
using System.Collections.Generic;
using System.IO;
using Oxide.CSharp.Patching;

namespace Oxide.CSharp.CompilerStream
{
    [Serializable]
    public class CompilerFile
    {
        public string Name { get; set; }
        public byte[] Data { get; set; }

        [NonSerialized]
        internal DateTime LastRead;

        [NonSerialized]
        internal bool KeepCached = false;

        [NonSerialized]
        internal static readonly Dictionary<string, CompilerFile> FileCache =
            new Dictionary<string, CompilerFile>(StringComparer.InvariantCultureIgnoreCase);

        public static CompilerFile CachedReadFile(string directory, string fileName, byte[] data = null)
        {
            string fullPath = Path.Combine(directory, fileName);

            CompilerFile file;
            lock (FileCache)
            {
                if (FileCache.TryGetValue(fullPath, out file))
                {
                    if (data != null)
                    {
                        file.Data = data;
                    }
                    file.LastRead = DateTime.Now;
                    return file;
                }
            }

            bool isPatched = false;

            if (data == null && File.Exists(fullPath))
            {
                data = Patcher.Run(File.ReadAllBytes(fullPath), out isPatched);
            }

            if (data == null)
            {
                return null;
            }

            file = new CompilerFile(fileName, data);
            file.LastRead = DateTime.Now;
            file.KeepCached = isPatched;
            lock (FileCache)
            {
                FileCache[fullPath] = file;
            }

            return file;
        }

        internal CompilerFile(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        internal CompilerFile(string directory, string name)
        {
            Name = name;
            Data = File.ReadAllBytes(Path.Combine(directory, Name));
        }

        internal CompilerFile(string path)
        {
            Name = Path.GetFileName(path);
            Data = File.ReadAllBytes(path);
        }
    }
}
