extern alias References;
using System.IO;
using System.Text;
using References::Newtonsoft.Json;

namespace Oxide.CSharp;

public class Serializer
{
    private readonly JsonSerializer _jsonSerializer;
    private readonly UTF8Encoding _encoding;

    public Serializer()
    {
        _jsonSerializer = new JsonSerializer();
        _encoding = new UTF8Encoding(false);
    }

    public void SerializeToStream<T>(MemoryStream memoryStream, T type, int bufferSize) where T : class
    {
        using StreamWriter streamWriter = new(memoryStream, _encoding, bufferSize, leaveOpen: true);

        _jsonSerializer.Serialize(streamWriter, type);

        streamWriter.Flush();
    }

    public byte[] Serialize<T>(T type) where T : class
    {
        using MemoryStream memoryStream = new();
        using StreamWriter streamWriter = new(memoryStream, _encoding);
        _jsonSerializer.Serialize(streamWriter, type);
        streamWriter.Flush();
        return memoryStream.ToArray();
    }

    public T Deserialize<T>(byte[] data) where T : class
    {
        using MemoryStream memoryStream = new(data);
        using StreamReader streamReader = new(memoryStream, _encoding);
        return (T)_jsonSerializer.Deserialize(streamReader, typeof(T));
    }
}
