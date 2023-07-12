extern alias References;

using References::Mono.Cecil;

namespace Oxide.CSharp.Patching.Validation
{
    public class IsHideBySig : PatchValidationAttribute
    {
        protected override bool IsValid(object item)
        {
            if (item is IMemberDefinition)
            {
                return GetPropertyValue(item, "IsHideBySig", false);
            }

            return false;
        }
    }
}
