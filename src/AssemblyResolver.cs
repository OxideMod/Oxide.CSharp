extern alias References;

using Oxide.Core;
using References::Mono.Cecil;

namespace Oxide.CSharp
{
    internal class AssemblyResolver : BaseAssemblyResolver
    {
        public AssemblyResolver() : base()
        {
            AddSearchDirectory(Interface.Oxide.ExtensionDirectory);
        }
    }
}
