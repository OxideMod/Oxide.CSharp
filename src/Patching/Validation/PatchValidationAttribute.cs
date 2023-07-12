using System;
using System.Reflection;

namespace Oxide.CSharp.Patching.Validation
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public abstract class PatchValidationAttribute : Attribute
    {
        public bool InverseCheck { get; set; }

        protected abstract bool IsValid(object item);

        public bool Validate(object item) => InverseCheck ? !IsValid(item) : IsValid(item);

        protected static T GetPropertyValue<T>(object instance, string name, T defaultValue = default, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
        {
            if (instance == null || string.IsNullOrEmpty(name))
            {
                return defaultValue;
            }

            Type type = instance.GetType();
            PropertyInfo prop = type.GetProperty(name, flags);

            if (prop == null)
            {
                return defaultValue;
            }

            object value = prop.GetValue(instance, null);

            if (value is T t)
            {
                return t;
            }

            return defaultValue;
        }
    }
}
