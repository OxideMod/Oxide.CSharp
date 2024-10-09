extern alias References;
using System.IO;
using Oxide.Core;
using References::Mono.Cecil;

namespace Oxide.CSharp;

internal class AssemblyResolver : IAssemblyResolver
{
    private readonly DefaultAssemblyResolver _resolver;
    private readonly AssemblyDefinition _mscorlib;

    public AssemblyResolver()
    {
        _resolver = new DefaultAssemblyResolver();
        _resolver.AddSearchDirectory(Interface.Oxide.ExtensionDirectory);
        _mscorlib = AssemblyDefinition.ReadAssembly(Path.Combine(Interface.Oxide.ExtensionDirectory, "mscorlib.dll"));
    }

    public AssemblyDefinition Resolve(AssemblyNameReference assemblyNameReference)
    {
        if (assemblyNameReference.Name == "System.Private.CoreLib")
        {
            Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Warning,
                new Logging.LogEvent(50, "Resolve"), "Resolver",
                "Redirecting reference to System.Private.CoreLib to mscorlib");

            return _mscorlib;
        }

        return _resolver.Resolve(assemblyNameReference);
    }

    public AssemblyDefinition Resolve(AssemblyNameReference assemblyNameReference, ReaderParameters readerParameters)
    {
        if (assemblyNameReference.Name == "System.Private.CoreLib")
        {
            Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Warning,
                new Logging.LogEvent(50, "Resolve"), "Resolver",
                "Redirecting reference to System.Private.CoreLib to mscorlib");

            return _mscorlib;
        }

        return _resolver.Resolve(assemblyNameReference, readerParameters);
    }

    public void Dispose()
    {
        _resolver.Dispose();
        _mscorlib.Dispose();
    }
}
