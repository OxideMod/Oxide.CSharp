extern alias References;

using References::Mono.Cecil;

namespace Oxide.CSharp.Patching.Validation
{
    public class SkipSpecialNameAttribute : PatchValidationAttribute
    {
        public override bool IsValid(object item)
        {
            if (item is IMemberDefinition member)
            {
                return !member.IsSpecialName;
            }

            return true;
        }
    }
}
