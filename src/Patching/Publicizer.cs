extern alias References;

using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;

namespace Oxide.CSharp.Patching
{
    [InverseNameValidation("0Harmony")]
    [InverseNameValidation("System")]
    [InverseNameValidation("Microsoft")]
    [InverseNameValidation("mscorlib")]
    [InverseNameValidation("Unity")]
    [InverseNameValidation("Mono")]
    [InverseNameValidation("netstandard")]
    [InverseNameValidation("Oxide")]
    [InverseNameValidation("MySql.Data")]
    public class Publicizer : TraversePatch
    {
        [HasVisability(false)]
        [SkipSpecialName]
        [DoesNotHaveAttribute("CompilerGeneratedAttribute", StringValidationType.EndsWith)]
        [DoesNotHaveAttribute("CompilerServices.ExtensionAttribute", StringValidationType.EndsWith)]
        protected override bool OnMemberDefinition(IMemberDefinition member)
        {
            return base.OnMemberDefinition(member);
        }

        protected override bool OnTypeDefinition(TypeDefinition type)
        {
            if (type.IsNested && !type.IsNestedPublic)
            {
                type.IsNestedPrivate = true;
                return true;
            }

            if (!type.IsPublic)
            {
                type.IsPublic = true;
                return true;
            }

            return false;
        }

        protected override bool OnFieldDefinition(FieldDefinition field)
        {
            if (field.IsPublic)
            {
                return false;
            }

            field.IsPublic = true;
            return true;
        }

        protected override bool OnPropertyDefinition(PropertyDefinition property)
        {
            bool get = property.GetMethod != null ? OnMethodDefinition(property.GetMethod) : false;
            bool set = property.SetMethod != null ? OnMethodDefinition(property.SetMethod) : false;

            return get || set;
        }

        protected override bool OnMethodDefinition(MethodDefinition method)
        {
            if (method.IsPublic)
            {
                return false;
            }

            method.IsPublic = true;
            return true;
        }
    }
}
