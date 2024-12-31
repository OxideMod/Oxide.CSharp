extern alias References;

using References::Mono.Cecil;

namespace Oxide.CSharp.Patching.Validation
{
    public class HasVisibilityAttribute : PatchValidationAttribute
    {
        public bool IsPublic { get; }

        public bool? IsStatic { get; set; }

        public HasVisibilityAttribute(bool isPublic)
        {
            IsPublic = isPublic;
        }

        protected override bool IsValid(object item)
        {
            if (item is TypeDefinition type)
            {
                if (type.IsNested)
                {
                    if (type.IsNestedPublic != IsPublic)
                    {
                        return false;
                    }
                }
                else
                {
                    if (type.IsPublic != IsPublic)
                    {
                        return false;
                    }
                }

                if (IsStatic.HasValue)
                {
                    bool stat = type.IsAbstract && type.IsSealed;

                    if (stat != IsStatic.Value)
                    {
                        return false;
                    }
                }

                return true;
            }
            else if (item is PropertyDefinition prop)
            {
                if (IsPublic)
                {
                    return prop.SetMethod != null ? IsValid(prop.GetMethod) && IsValid(prop.SetMethod) : IsValid(prop.GetMethod);
                }
                else
                {
                    return prop.SetMethod != null ? IsValid(prop.GetMethod) || IsValid(prop.SetMethod) : IsValid(prop.GetMethod);
                }
            }
            else if (item is EventDefinition @event)
            {
                return @event.AddMethod != null && IsValid(@event.AddMethod);
            }
            else if (item is MethodDefinition method)
            {
                if (IsStatic.HasValue)
                {
                    if (method.IsStatic != IsStatic.Value)
                    {
                        return false;
                    }
                }

                return method.IsPublic == IsPublic;
            }
            else if (item is FieldDefinition field)
            {
                if (IsStatic.HasValue)
                {
                    if (field.IsStatic != IsStatic.Value)
                    {
                        return false;
                    }
                }

                return field.IsPublic == IsPublic;
            }
            else if  (item is IMemberDefinition)
            {
                bool? isPub = GetPropertyValue<bool?>(item, "IsPublic");

                if (!isPub.HasValue || isPub.Value != IsPublic)
                {
                    return false;
                }

                if (IsStatic.HasValue)
                {
                    bool? isStat = GetPropertyValue<bool?>(item, "IsStatic");

                    if (!isStat.HasValue || isStat.Value != IsStatic.Value)
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }
    }
}
