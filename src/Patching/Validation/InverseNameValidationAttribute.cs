using System;

namespace Oxide.CSharp.Patching.Validation
{
    public class InverseNameValidationAttribute : NameValidationAttribute
    {
        public InverseNameValidationAttribute(string rule, StringValidationType type = StringValidationType.StartsWith, StringComparison comparison = StringComparison.InvariantCultureIgnoreCase) : base(rule, type, comparison)
        {
        }

        public override bool IsValid(object item)
        {
            return !base.IsValid(item);
        }
    }
}
