extern alias References;
using System.IO;
using Oxide.CSharp.Common;
using References::Newtonsoft.Json;

namespace Oxide.CSharp;

internal class Serializer
{
    private readonly JsonSerializer _jsonSerializer;

    internal Serializer()
    {
        _jsonSerializer = new JsonSerializer();
    }

    internal byte[] Serialize<T>(T type) where T : class
    {
        using MemoryStream memoryStream = new();
        using StreamWriter streamWriter = new(memoryStream, Constants.CompilerEncoding);
        _jsonSerializer.Serialize(streamWriter, type);
        streamWriter.Flush();
        return memoryStream.ToArray();
    }

    internal T? Deserialize<T>(byte[] data) where T : class
    {
        using MemoryStream memoryStream = new(data);
        using StreamReader streamReader = new(memoryStream, Constants.CompilerEncoding);
        return (T?)_jsonSerializer.Deserialize(streamReader, typeof(T));
    }

    internal JsonSerializer GetJsonSerializer() => _jsonSerializer;
}
