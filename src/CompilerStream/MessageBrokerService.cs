extern alias References;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Oxide.Core;
using Oxide.CSharp.Common;

namespace Oxide.CSharp.CompilerStream
{
    internal class MessageBrokerService
    {
        private const int DefaultMaxBufferSize = 1024;

        private readonly Pooling.IArrayPool<byte> _arrayPool;
        private NamedPipeServerStream _pipeServer;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private int _messageId;

        public event Action<CompilerMessage> OnMessageReceived;

        public MessageBrokerService()
        {
            _arrayPool = Pooling.ArrayPool<byte>.Shared;
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

        public void SendMessage(CompilerMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            WriteMessage(message);
        }

        private void WriteMessage(CompilerMessage message)
        {
            byte[] data = Constants.Serializer.Serialize(message);
            byte[] buffer = _arrayPool.Take(data.Length + sizeof(int));
            try
            {
                int destinationIndex = data.Length.WriteBigEndian(buffer);
                Array.Copy(data, 0, buffer, destinationIndex, data.Length);
                OnWrite(buffer, 0, buffer.Length);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError($"Error sending message to compiler: {exception}");
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
        }

        private CompilerMessage? ReadMessage()
        {
            byte[] buffer = _arrayPool.Take(sizeof(int));
            int read = 0;
            try
            {
                while (read < buffer.Length)
                {
                    read += OnRead(buffer, read, buffer.Length - read);
                    if (read == 0)
                    {
                        return null;
                    }
                }

                int length = buffer.ReadBigEndian();
                byte[] buffer2 = _arrayPool.Take(length);
                read = 0;
                try
                {
                    while (read < length)
                    {
                        read += OnRead(buffer2, read, length - read);
                    }

                    return Constants.Serializer.Deserialize<CompilerMessage>(buffer2);
                }
                finally
                {
                    _arrayPool.Return(buffer2);
                }
            }
            catch (Exception exception)
            {
                if (exception is ObjectDisposedException)
                {
                    return null;
                }

                Interface.Oxide.LogError($"Error reading message: {exception}");
                return null;
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
        }

        private void OnWrite(byte[] buffer, int index, int count)
        {
            Validate(buffer, index, count);

            int remaining = count;
            int written = 0;
            while (remaining > 0)
            {
                int toWrite = Math.Min(DefaultMaxBufferSize, remaining);
                _pipeServer.Write(buffer, index + written, toWrite);
                remaining -= toWrite;
                written += toWrite;
                _pipeServer.Flush();
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

        public int SendShutdownMessage()
        {
            CompilerMessage message = new CompilerMessage
            {
                Id = _messageId++,
                Type = MessageType.Shutdown,
            };

            SendMessage(message);
            return message.Id;
        }

        public int SendCompileMessage(CompilerData project)
        {
            CompilerMessage message = new CompilerMessage
            {
                Id = _messageId++,
                Type = MessageType.Data,
                Data = Constants.Serializer.Serialize(project)
            };

            SendMessage(message);
            return message.Id;
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
            _pipeServer.Disconnect();
            _pipeServer.Dispose();
        }
    }
}
