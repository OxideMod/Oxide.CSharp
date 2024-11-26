extern alias References;
using System.IO;
using System.Text;
using Oxide.CSharp.Interfaces;
using References::Newtonsoft.Json;

namespace Oxide.CSharp.CompilerStream
{
    internal class Serializer : ISerializer
    {
        private readonly JsonSerializer _jsonSerializer;

        internal Serializer()
        {
            _jsonSerializer = new JsonSerializer();
        }

        public byte[] Serialize<T>(T type) where T : class
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (StreamWriter writer = new StreamWriter(memoryStream, Encoding.UTF8))
                {
                    _jsonSerializer.Serialize(writer, type);
                    writer.Flush();
                    return memoryStream.ToArray();
                }
            }
        }

        public T Deserialize<T>(byte[] data) where T : class
        {
            using (MemoryStream memoryStream = new MemoryStream(data))
            {
                using (StreamReader reader = new StreamReader(memoryStream, Encoding.UTF8))
                {
                    return (T)_jsonSerializer.Deserialize(reader, typeof(T));
                }
            }
        }
    }
}
