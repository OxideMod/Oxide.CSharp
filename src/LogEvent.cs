namespace Oxide.Logging
{
    /// <summary>
    /// A log event, used to help identify certain logs messages
    /// </summary>
    public struct LogEvent
    {
        /// <summary>
        /// The event Id
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// The event name
        /// </summary>
        public string Name { get; }

        internal LogEvent(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public static LogEvent Compile { get; } = new LogEvent(4, "Compile");

        public static LogEvent HookCall { get; } = new LogEvent(10, "ExecuteHook");

        public static LogEvent Patch { get; } = new LogEvent(23, "Patching");
    }
}
