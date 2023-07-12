extern alias References;

using References::Mono.Cecil;

namespace Oxide.CSharp.Patching.Validation
{
    public class IsSpecialNameAttribute : PatchValidationAttribute
    {
        protected override bool IsValid(object item)
        {
            if (item is IMemberDefinition member)
            {
                return member.IsSpecialName;
            }

            return false;
        }
    }
}
