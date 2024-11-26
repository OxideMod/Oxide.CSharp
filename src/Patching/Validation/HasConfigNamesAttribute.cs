extern alias References;

using Oxide.Core;

namespace Oxide.CSharp.Patching.Validation
{
    public class HasConfigNamesAttribute : HasNameAttribute
    {
        public HasConfigNamesAttribute() : base(string.Empty)
        {
        }

        protected override bool IsValid(object item)
        {
            foreach (string reference in Interface.Oxide.Config.Compiler.IgnoredPublicizerReferences)
            {
                ValidationRule = reference;

                if (base.IsValid(item))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
