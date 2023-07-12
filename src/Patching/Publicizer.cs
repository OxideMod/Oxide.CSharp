extern alias References;

using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;

namespace Oxide.CSharp.Patching
{
    [HasName("0Harmony", InverseCheck = true)]
    [HasName("System", InverseCheck = true)]
    [HasName("Microsoft", InverseCheck = true)]
    [HasName("mscorlib", InverseCheck = true)]
    [HasName("Unity", InverseCheck = true)]
    [HasName("Mono", InverseCheck = true)]
    [HasName("netstandard", InverseCheck = true)]
    [HasName("Oxide", InverseCheck = true)]
    [HasName("MySql.Data", InverseCheck = true)]
    [HasEnvironmentalVariable("AllowPublicize")]
    public class Publicizer : TraversePatch
    {
        [HasVisability(false)]
        [IsSpecialName(InverseCheck = false)]
        [HasAttribute("CompilerGeneratedAttribute", StringValidationType.EndsWith, InverseCheck = true)]
        [HasAttribute("CompilerServices.ExtensionAttribute", StringValidationType.EndsWith, InverseCheck = true)]
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
