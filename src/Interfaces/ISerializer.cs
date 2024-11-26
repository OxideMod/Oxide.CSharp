namespace Oxide.CSharp.Interfaces
{
    internal interface ISerializer
    {
        internal byte[] Serialize<T>(T type) where T : class;

        internal T Deserialize<T>(byte[] data) where T : class;
    }
}
