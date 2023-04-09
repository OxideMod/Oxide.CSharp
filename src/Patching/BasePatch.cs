extern alias References;

using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.Logging;
using References::Mono.Cecil;

namespace Oxide.CSharp.Patching
{
    internal abstract class BasePatch : IPatch
    {
        public abstract string Name { get; }

        public abstract bool TryPatch(ModuleDefinition module);

        public void Log(LogType level, string message) => Interface.Oxide.RootLogger.WriteDebug(level, LogEvent.Patch, Name, message);
    }
}
