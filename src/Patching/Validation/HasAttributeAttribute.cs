extern alias References;
using System;
using System.Linq;
using References::Mono.Cecil;
using References::Mono.Collections.Generic;

namespace Oxide.CSharp.Patching.Validation
{
    public class HasAttributeAttribute : HasNameAttribute
    {
        public HasAttributeAttribute(string rule, StringValidationType type = StringValidationType.StartsWith,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase) : base(rule, type, comparison)
        {
        }

        protected override bool IsValid(object item) => item switch
        {
            CustomAttribute attribute => base.IsValid(attribute.AttributeType.FullName),
            Collection<CustomAttribute> attributes => attributes.Any(a => base.IsValid(a.AttributeType.FullName)),
            AssemblyDefinition { HasCustomAttributes: true } assem => assem.CustomAttributes.Any(a => base.IsValid(a.AttributeType.FullName)),
            ModuleDefinition { HasCustomAttributes: true } module => module.CustomAttributes.Any(a => base.IsValid(a.AttributeType.FullName)),
            IMemberDefinition { HasCustomAttributes: true } member => member.CustomAttributes.Any(a => base.IsValid(a.AttributeType.FullName)),
            _ => false
        };
    }
}
