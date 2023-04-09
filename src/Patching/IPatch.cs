extern alias References;

using References::Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Oxide.CSharp.Patching
{
    public interface IPatch
    {
        string Name { get; }

        bool TryPatch(ModuleDefinition module);
    }
}
