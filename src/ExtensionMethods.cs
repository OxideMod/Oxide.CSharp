using Oxide.Core.Logging;
using Oxide.Logging;
using System;
using Oxide.Core;

namespace Oxide
{
    /// <summary>
    /// Useful extension methods which are added to base types
    /// </summary>
    public static class ExtensionMethods
    {
        public static void WriteDebug(this Logger logger, LogType level, LogEvent? @event, string source, string message, Exception exception = null)
        {
#if DEBUG
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string msg = "[DEBUG] ";

            if (!string.IsNullOrEmpty(source))
            {
                msg += $"[{source}] ";
            }

            if (@event.HasValue)
            {
                msg += $"[{@event.Value.Id}] ";
            }

            msg += message;

            if (exception != null)
            {
                Interface.Oxide.RootLogger.WriteException(msg, exception);
            }
            else
            {
                Interface.Oxide.RootLogger.Write(level, msg);
            }
#endif
        }

        internal static int WriteBigEndian(this int value, byte[] array, int startPos = 0)
        {
            array[startPos]     = (byte)(value >> 24);
            array[startPos + 1] = (byte)(value >> 16);
            array[startPos + 2] = (byte)(value >> 8);
            array[startPos + 3] = (byte)value;
            return sizeof(int);
        }

        internal static int ReadBigEndian(this byte[] array, int startPos = 0)
        {
            return (array[startPos] << 24) | (array[startPos + 1] << 16) | (array[startPos + 2] << 8) | array[startPos + 3];
        }

        internal static int WriteLittleEndian(this int value, byte[] array, int startPos = 0)
        {
            array[startPos] = (byte)value;
            array[startPos + 1] = (byte)(value >> 8);
            array[startPos + 2] = (byte)(value >> 16);
            array[startPos + 3] = (byte)(value >> 24);
            return sizeof(int);
        }

        internal static int ReadLittleEndian(this byte[] array, int startPos = 0)
        {
            return array[startPos] | (array[startPos + 1] << 8) | (array[startPos + 2] << 16) | (array[startPos + 3] << 24);
        }
    }
}
