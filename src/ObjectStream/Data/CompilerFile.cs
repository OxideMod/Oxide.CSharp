using System;
using System.Collections.Generic;
using System.IO;

namespace ObjectStream.Data
{
    [Serializable]
    public class CompilerFile
    {
        [NonSerialized]
        internal readonly static Dictionary<string, CompilerFile> FileCache = new Dictionary<string, CompilerFile>(StringComparer.InvariantCultureIgnoreCase);

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
            
            if (data == null && File.Exists(fullPath))
            {
                data = File.ReadAllBytes(fullPath);
            }

            if (data == null)
            {
                return null;
            }

            file = new CompilerFile(fileName, data);
            file.LastRead = DateTime.Now;
            lock (FileCache)
            {
                FileCache[fullPath] = file;
            }

            return file;
        }

        public string Name { get; set; }
        public byte[] Data { get; set; }

        [NonSerialized]
        internal DateTime LastRead;

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
