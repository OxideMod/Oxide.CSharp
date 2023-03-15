using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.Logging;
using System;

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
    }
}
