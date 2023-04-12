extern alias References;

using References::Mono.Cecil;

namespace Oxide.CSharp.Patching
{
    public interface IPatch
    {
        /// <summary>
        /// Runs a patch on the given <see cref="AssemblyDefinition"/>
        /// </summary>
        /// <param name="context">The patch context</param>
        /// <returns>The number of patched completed on this module</returns>
        void Patch(PatchContext context);
    }
}
