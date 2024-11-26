using System;

namespace Oxide.CSharp.Patching.Validation
{
    public class HasEnvironmentalVariableAttribute : PatchValidationAttribute
    {
        private string VariableName { get; }

        public HasEnvironmentalVariableAttribute(string rule)
        {
            VariableName = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        protected override bool IsValid(object item) => !string.IsNullOrEmpty(EnvironmentHelper.GetVariable(VariableName));
    }
}
