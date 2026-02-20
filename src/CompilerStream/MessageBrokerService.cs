extern alias References;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Oxide.CompilerServices;
using Oxide.Core;
using Oxide.CSharp.Common;
using Oxide.Pooling;

namespace Oxide.CSharp.CompilerStream
{
    internal class MessageBrokerService
    {
        private const int DefaultMaxBufferSize = 1024;

        private NamedPipeServerStream _pipeServer;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private int _messageId;

        public event Action<CompilerMessage> OnMessageReceived;

        public MessageBrokerService()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Start(string pipeName)
        {
            _pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            _pipeServer.WaitForConnection();

            CancellationToken cancellationToken = _cancellationTokenSource.Token;
            Task.Run(() => WorkerAsync(cancellationToken), cancellationToken);
        }

        public void SendMessage(CompilerMessage message) => WriteMessage(message);

        public int SendShutdownMessage()
        {
            CompilerMessage message = new()
            {
                Id = _messageId++,
                Type = MessageType.Shutdown,
            };

            SendMessage(message);
            return message.Id;
        }

        private async Task WorkerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_pipeServer.IsConnected)
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                bool processed = false;

                if (OnMessageReceived != null)
                {
                    try
                    {
                        CompilerMessage? compilerMessage = ReadMessage();
                        if (compilerMessage != null)
                        {
                            OnMessageReceived(compilerMessage);
                            processed = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        Interface.Oxide.LogError($"Error reading message queue: {exception}");
                    }
                }

                if (!processed)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private void WriteMessage(CompilerMessage message)
        {
            byte[] headerBuffer = ArrayPool<byte>.Shared.Take(sizeof(int));

            using MemoryStream memoryStream = new();
            using StreamWriter streamWriter = new(memoryStream, Constants.CompilerEncoding, DefaultMaxBufferSize);
            try
            {
                Constants.Serializer.GetJsonSerializer().Serialize(streamWriter, message);

                streamWriter.Flush();

                int length = (int)memoryStream.Length;
                length.WriteBigEndian(headerBuffer);

                _pipeServer.Write(headerBuffer, 0, sizeof(int));
                _pipeServer.Write(memoryStream.GetBuffer(), 0, length);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError($"Error sending message to compiler: {exception}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBuffer);
            }
        }

        private CompilerMessage? ReadMessage()
        {
            byte[] headerBuffer = ArrayPool<byte>.Shared.Take(sizeof(int));
            int read = 0;
            try
            {
                while (read < headerBuffer.Length)
                {
                    read += OnRead(headerBuffer, read, headerBuffer.Length - read);
                    if (read == 0)
                    {
                        return null;
                    }
                }

                int length = headerBuffer.ReadBigEndian();
                byte[] messageBuffer = ArrayPool<byte>.Shared.Take(length);
                try
                {

                    read = 0;
                    while (read < length)
                    {
                        read += OnRead(messageBuffer, read, length - read);
                    }

                    return Constants.Serializer.Deserialize<CompilerMessage>(messageBuffer);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(messageBuffer);
                }
            }
            catch (Exception exception)
            {
                if (exception is ObjectDisposedException)
                {
                    return null;
                }

                Interface.Oxide.LogError($"Error reading message from compiler: {exception}");
                return null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBuffer);
            }
        }

        private int OnRead(byte[] buffer, int index, int count)
        {
            Validate(buffer, index, count);

            int read = 0;
            int remaining = count;

            while (remaining > 0)
            {
                int toRead = Math.Min(DefaultMaxBufferSize, remaining);
                int r = _pipeServer.Read(buffer, index + read, toRead);

                if (r == 0 && read == 0)
                {
                    return 0;
                }

                read += r;
                remaining -= r;
            }

            return read;
        }

        private void Validate(byte[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Value must be zero or greater");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Value must be zero or greater");
            }

            if (index + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException($"{nameof(index)} + {nameof(count)}",
                    "Attempted to read more than buffer can allow");
            }
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _pipeServer.Disconnect();
            _pipeServer.Dispose();
        }
    }
}
