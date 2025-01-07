extern alias References;

using References::Mono.Cecil;
using System;
using System.Text.RegularExpressions;

namespace Oxide.CSharp.Patching.Validation
{
    public class HasNameAttribute : PatchValidationAttribute
    {
        public string ValidationRule { get; internal set; }

        public StringValidationType ValidationType { get; }

        public StringComparison ValidationComparison { get; }

        public HasNameAttribute(string rule, StringValidationType type = StringValidationType.StartsWith, StringComparison comparison = StringComparison.InvariantCultureIgnoreCase)
        {
            ValidationRule = rule;
            ValidationType = type;
            ValidationComparison = comparison;
        }

        protected override bool IsValid(object item)
        {
            string name = null;

            if (item is string str)
            {
                name = str;
            }
            else if (item is AssemblyDefinition assem)
            {
                name = assem.FullName;
            }
            else if (item is ModuleDefinition moduleDef)
            {
                name = moduleDef.Assembly.FullName;
            }
            else if (item is ModuleReference module)
            {
                name = module.Name;
            }
            else if (item is AssemblyNameReference assName)
            {
                name = assName.FullName;
            }
            else if (item is MemberReference member)
            {
                name = member.FullName;
            }
            else
            {
                return false;
            }

            switch (ValidationType)
            {
                case StringValidationType.Equals:
                    return name.Equals(ValidationRule, ValidationComparison);

                case StringValidationType.Contains:
                    return name.IndexOf(ValidationRule, ValidationComparison) >= 0;

                default:
                case StringValidationType.StartsWith:
                    return name.StartsWith(ValidationRule, ValidationComparison);

                case StringValidationType.EndsWith:
                    return name.EndsWith(ValidationRule, ValidationComparison);

                case StringValidationType.RegularExpression:
                    return Regex.IsMatch(name, ValidationRule, RegexOptions.Compiled);
            }
        }
    }
}
