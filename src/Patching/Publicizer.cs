extern alias References;
using System;
using System.IO;
using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;
using System;
using System.IO;

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
    [HasConfigNames(InverseCheck = true)]
    [HasEnvironmentalVariable("AllowPublicize")]
    public class Publicizer : TraversePatch
    {
        [HasVisibility(false)]
        // [IsSpecialName(InverseCheck = false)]
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
                type.IsNestedPublic = true;
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

        protected override void OnPatchFinished(PatchContext context)
        {
            string writePath = EnvironmentHelper.GetVariable("PublicizerOutput");

            if (!string.IsNullOrEmpty(writePath))
            {
                string name = context.Assembly.Name.Name;
                if (!Directory.Exists(writePath))
                {
                    Log($"Failed to write {name} because PublicizeOutput {writePath} doesn't exist", Core.Logging.LogType.Error);
                    return;
                }

                try
                {
                    name = Path.Combine(writePath, name + ".dll");
                    context.Assembly.Write(name);
                    Log($"Wrote publicized assembly to {writePath}");
                }
                catch (Exception e)
                {
                    Log($"Failed to write publicized assembly to {writePath}", Core.Logging.LogType.Error, e);
                }
            }
        }
    }
}
