extern alias References;

using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;
using System.Collections.Generic;

namespace Oxide.CSharp.Patching
{
    public class PatchContext
    {
        public AssemblyDefinition Assembly { get; }

        public List<PatchValidationAttribute> PatchValidators { get; internal set; }

        public int TotalPatches { get; internal set; }

        public int ContextPatches { get; internal set; }

        public PatchContext(AssemblyDefinition assembly)
        {
            Assembly = assembly;
        }

        public void IncrementPatches()
        {
            ContextPatches++;
            TotalPatches++;
        }
    }
}
