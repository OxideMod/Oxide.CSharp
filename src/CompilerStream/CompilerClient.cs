using ObjectStream.Data;
using Oxide.IO;
using Oxide.IO.Serialization;
using Oxide.IO.TransportMethods;
using Oxide.Pooling;
using System;
using System.Diagnostics;
using System.Text;

namespace Oxide.CSharp.CompilerStream
{
    public class CompilerClient
    {
        public event Action OnHeatbeat;

        public event Action<Version> OnVersionUpdate;

        public event Action<int, CompilationResult> OnResult;

        #region Information

        private int MessageId { get; set; } = 0;

        public DateTime LastHeartbeat { get; private set; }

        public Version Version { get; private set; }

        #endregion


        #region Serializers

        private ISerializer<CompilerMessage, byte[]> MessageSerializer { get; }

        private ISerializer<CompilationResult, byte[]> ResultsSerializer { get; }

        private ISerializer<CompilerData, byte[]> CompilationSerializer { get; }

        #endregion

        private MessageBroker<CompilerMessage> MessageBroker { get; }

        public CompilerClient(Process process)
        {
            MessageSerializer = new JsonSerializer<CompilerMessage>(Encoding.UTF8, PoolFactory<StringBuilder>.Default);
            ResultsSerializer = new JsonSerializer<CompilationResult>(Encoding.UTF8, PoolFactory<StringBuilder>.Default);
            CompilationSerializer = new JsonSerializer<CompilerData>(Encoding.UTF8, PoolFactory<StringBuilder>.Default);
            ProcessTransportProtocol protocol = new ProcessTransportProtocol(process, killOnDispose: true);
            MessageBroker = new MessageBroker<CompilerMessage>(protocol, protocol, MessageSerializer);
            MessageBroker.OnMessageReceived += OnMessage;
        }

        private void OnMessage(CompilerMessage message)
        {
            if (HasFlag(message, MessageType.Heartbeat))
            {
                LastHeartbeat = DateTime.Now;
                OnHeatbeat?.Invoke();
            }

            if (HasFlag(message, MessageType.VersionInfo))
            {
                string versionStr = Encoding.UTF8.GetString(message.Data);
                Version = new Version(versionStr);
                OnVersionUpdate?.Invoke(Version);
            }
            else if (HasFlag(message, MessageType.Data))
            {
                CompilationResult result = ResultsSerializer.Deserialize(message.Data);
                OnResult?.Invoke(message.Id, result);
            }
            else if (HasFlag(message, MessageType.Error))
            {
                // TODO: Implement Errors
            }
        }

        public int Compile(CompilerData project)
        {
            CompilerMessage message = new CompilerMessage()
            {
                Id = MessageId++,
                Type = MessageType.Data,
                Data = CompilationSerializer.Serialize(project)
            };

            MessageBroker.SendMessage(message);
            return message.Id;
        }

        public void Stop()
        {
            MessageBroker.Dispose();
        }

        private static bool HasFlag(CompilerMessage message, MessageType type) => ((message.Type & type) == type);
    }
}
