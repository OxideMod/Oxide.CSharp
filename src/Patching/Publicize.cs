extern alias References;

using Oxide.Core.Logging;
using References::Mono.Cecil;

namespace Oxide.CSharp.Patching
{
    internal class Publicize : BasePatch
    {
        public override string Name { get; } = nameof(Publicize);

        public int Patched { get; protected set; }

        public override bool TryPatch(ModuleDefinition module)
        {
            if (module.HasTypes)
            {

                for (int i = 0; i < module.Types.Count; i++)
                {
                    ProcessType(module.Types[i]);
                }
            }

            if (Patched != 0)
            {
                Log(LogType.Info, $"{Name}d {Patched} Types/Members on {module.Assembly.Name.Name}");
            }

            return Patched != 0;
        }

        protected virtual void ProcessType(TypeDefinition type)
        {
            if (!type.IsPublic && !type.IsNestedPublic)
            {
                Patched++;
                if (type.IsNested)
                {
                    type.IsNestedPublic = true;
                }
                else
                {
                    type.IsPublic = true;
                }

                //Log(LogType.Info, $"Type '{type.FullName}' has been made public");
            }

            if (type.HasFields)
            {
                for (int i = 0; i < type.Fields.Count; i++)
                {
                    ProcessField(type.Fields[i]);
                }
            }

            if (type.HasProperties)
            {
                for (int i = 0; i < type.Properties.Count; i++)
                {
                    ProcessProperty(type.Properties[i]);
                }
            }

            if (type.HasMethods)
            {
                for (int i = 0; i < type.Methods.Count; i++)
                {
                    ProcessMethod(type.Methods[i]);
                }
            }

            if (type.HasNestedTypes)
            {
                for (int i = 0; i < type.NestedTypes.Count; i++)
                {
                    ProcessType(type.NestedTypes[i]);
                }
            }
        }

        protected virtual void ProcessField(FieldDefinition field)
        {
            if (!field.IsPublic)
            {
                Patched++;
                field.IsPublic = true;
                //Log(LogType.Info, $"Field '{field.FullName}' has be made public");
            }
        }

        protected virtual void ProcessProperty(PropertyDefinition property)
        {
            bool changed = false;
            //string log = $"Property '{property.FullName}('";
            if (property.GetMethod != null && !property.GetMethod.IsPublic)
            {
                property.GetMethod.IsPublic = true;
                //log += "getter";
                changed = true;
            }

            if (property.SetMethod != null && !property.SetMethod.IsPublic)
            {
                property.SetMethod.IsPublic = true;
                //log += changed ? ", setter" : "setter";
                changed = true;
            }

            if (changed)
            {
                Patched++;
                //log += ") has been made public";
                //Log(LogType.Info, log);
            }
        }

        protected virtual void ProcessMethod(MethodDefinition method)
        {
            if (!method.IsPublic)
            {
                Patched++;
                method.IsPublic = true;
                //Log(LogType.Info, $"Method '{method.FullName}' has been made pubic");
            }
        }
    }
}
