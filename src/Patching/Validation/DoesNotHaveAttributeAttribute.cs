extern alias References;

using References::Mono.Cecil;
using References::Mono.Collections.Generic;
using System;
using System.Linq;

namespace Oxide.CSharp.Patching.Validation
{
    public class DoesNotHaveAttributeAttribute : NameValidationAttribute
    {
        public DoesNotHaveAttributeAttribute(string rule, StringValidationType type = StringValidationType.StartsWith, StringComparison comparison = StringComparison.InvariantCultureIgnoreCase) : base(rule, type, comparison)
        {
        }

        public override bool IsValid(object item)
        {
            if (item is string str)
            {
                return !base.IsValid(str);
            }
            else if (item is CustomAttribute attribute)
            {
                return !base.IsValid(attribute.AttributeType.FullName);
            }
            else if (item is Collection<CustomAttribute> attributes)
            {
                return !attributes.Any(a => base.IsValid(a.AttributeType.FullName));
            }
            else if (item is AssemblyDefinition assem && assem.HasCustomAttributes)
            {
                return !assem.CustomAttributes.Any(a => base.IsValid(a.AttributeType.FullName));
            }
            else if (item is ModuleDefinition module && module.HasCustomAttributes)
            {
                return !module.CustomAttributes.Any(a => base.IsValid(a.AttributeType.FullName));
            }
            else if (item is IMemberDefinition member && member.HasCustomAttributes)
            {
                return !member.CustomAttributes.Any(a => base.IsValid(a.AttributeType.FullName));
            }

            return true;
        }
    }
}
