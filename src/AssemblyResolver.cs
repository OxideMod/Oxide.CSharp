extern alias References;

using Oxide.Core;
using References::Mono.Cecil;
using System.IO;

namespace Oxide.CSharp
{
    internal class AssemblyResolver : DefaultAssemblyResolver
    {
        internal readonly AssemblyDefinition mscorlib;

        public AssemblyResolver() : base()
        {
            AddSearchDirectory(Interface.Oxide.ExtensionDirectory);
            mscorlib = AssemblyDefinition.ReadAssembly(Path.Combine(Interface.Oxide.ExtensionDirectory, "mscorlib.dll"));
            AddSearchDirectory(CompilerService.runtimePath);
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name.Name == "System.Private.CoreLib")
            {
                Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Warning, new Logging.LogEvent(50, "Resolve"), "Resolver", "Redirecting reference to System.Private.CoreLib to mscorlib");
                return mscorlib;
            }

            return base.Resolve(name, parameters);
        }
    }
}
