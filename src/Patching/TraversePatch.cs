extern alias References;

using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Oxide.CSharp.Patching
{
    public abstract class TraversePatch : IPatch
    {
        protected virtual string Name { get; }

        protected IEnumerable<PatchValidationAttribute> TypeValidators { get; }

        protected IEnumerable<PatchValidationAttribute> PropertyValidators { get; }

        protected IEnumerable<PatchValidationAttribute> FieldValidators { get; }

        protected IEnumerable<PatchValidationAttribute> MethodValidators { get; }

        protected IEnumerable<PatchValidationAttribute> EventValidators { get; }

        protected IEnumerable<PatchValidationAttribute> MemberValidators { get; }

        protected TraversePatch()
        {
            Type topType = GetType();
            Name = topType.Name;
            TypeValidators = GetValidationRules(nameof(OnTypeDefinition), topType);
            PropertyValidators = GetValidationRules(nameof(OnPropertyDefinition), topType);
            FieldValidators = GetValidationRules(nameof(OnFieldDefinition), topType);
            MethodValidators = GetValidationRules(nameof(OnMethodDefinition), topType);
            EventValidators = GetValidationRules(nameof(OnEventDefinition), topType);
            MemberValidators = GetValidationRules(nameof(OnMemberDefinition), topType);
        }

        public void Patch(PatchContext context)
        {
            List<TypeDefinition> types = context.Assembly.MainModule.GetTypes().ToList();

            for (int t = 0; t < types.Count; t++)
            {
                TypeDefinition type = types[t];
                RecurseType(type, context);
            }
        }

        private void RecurseType(TypeDefinition type, PatchContext context)
        {
            if (RunValidation(type, MemberValidators) && OnMemberDefinition(type))
            {
                context.IncrementPatches();
            }

            if (type.HasProperties)
            {
                for (int p = 0; p < type.Properties.Count; p++)
                {
                    PropertyDefinition prop = type.Properties[p];

                    if (RunValidation(prop, MemberValidators) && OnMemberDefinition(prop))
                    {
                        context.IncrementPatches();
                    }
                }
            }

            if (type.HasFields)
            {
                for (int f = 0; f < type.Fields.Count; f++)
                {
                    FieldDefinition field = type.Fields[f];

                    if (RunValidation(field, MemberValidators) && OnMemberDefinition(field))
                    {
                        context.IncrementPatches();
                    }
                }
            }

            if (type.HasMethods)
            {
                for (int m = 0; m < type.Methods.Count; m++)
                {
                    MethodDefinition method = type.Methods[m];

                    if (RunValidation(method, MemberValidators) && OnMemberDefinition(method))
                    {
                        context.IncrementPatches();
                    }
                }
            }

            if (type.HasEvents)
            {
                for (int e = 0; e < type.Events.Count; e++)
                {
                    EventDefinition @event = type.Events[e];

                    if (RunValidation(@event, MemberValidators) && OnMemberDefinition(@event))
                    {
                        context.IncrementPatches();
                    }
                }
            }

            if (type.HasNestedTypes)
            {
                for (int t = 0; t < type.NestedTypes.Count; t++)
                {
                    RecurseType(type.NestedTypes[t], context);
                }
            }
        }

        /// <summary>
        /// Called when a member is being traversed over
        /// </summary>
        /// <param name="member">The member</param>
        /// <returns>True if the member has been patched</returns>
        /// <remarks>Overriding this method will intercept all the calls to the other virtual methods</remarks>
        protected virtual bool OnMemberDefinition(IMemberDefinition member)
        {
            if (member is TypeDefinition type)
            {
                return RunValidation(member, TypeValidators) && OnTypeDefinition(type);
            }
            else if (member is PropertyDefinition prop)
            {
                return RunValidation(member, PropertyValidators) && OnPropertyDefinition(prop);
            }
            else if (member is FieldDefinition field)
            {
                return RunValidation(member, FieldValidators) && OnFieldDefinition(field);
            }
            else if (member is MethodDefinition method)
            {
                return RunValidation(method, MethodValidators) && OnMethodDefinition(method);
            }
            else if (member is EventDefinition @event)
            {
                return RunValidation(@event, EventValidators) && OnEventDefinition(@event);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Called when a type is being traversed over
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>True if a patch was applied</returns>
        protected virtual bool OnTypeDefinition(TypeDefinition type)
        {
            return false;
        }

        /// <summary>
        /// Called when a property is being traversed over
        /// </summary>
        /// <param name="property">The property</param>
        protected virtual bool OnPropertyDefinition(PropertyDefinition property)
        {
            return false;
        }

        /// <summary>
        /// Called when a field is being traversed over
        /// </summary>
        /// <param name="field">The field</param>
        /// <returns>True if a patch was applied</returns>
        protected virtual bool OnFieldDefinition(FieldDefinition field)
        {
            return false;
        }

        /// <summary>
        /// Called when a method is being traversed over
        /// </summary>
        /// <param name="method">The method</param>
        /// <returns>True if a patch was applied</returns>
        protected virtual bool OnMethodDefinition(MethodDefinition method)
        {
            return false;
        }

        /// <summary>
        /// Called when a event is being traversed over
        /// </summary>
        /// <param name="event">The event</param>
        /// <returns>True if a patch was applied</returns>
        protected virtual bool OnEventDefinition(EventDefinition @event)
        {
            return false;
        }

        protected bool RunValidation(IMemberDefinition member, IEnumerable<PatchValidationAttribute> validations)
        {
            if (member == null)
            {
                return false;
            }

            if (validations == null)
            {
                return true;
            }

            foreach (PatchValidationAttribute valid in validations)
            {
                if (!valid.IsValid(member))
                {
                    Type type = valid.GetType();
                    return false;
                }
            }

            return true;
        }

        protected void Log(string message, LogType logType = LogType.Info, Exception e = null)
        {
            Interface.Oxide.RootLogger.WriteDebug(logType, Logging.LogEvent.Patch, Name, message, e);
        }

        private static IEnumerable<PatchValidationAttribute> GetValidationRules(string methodName, Type type)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type ret = typeof(bool);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];

                if (method.Name.Equals(methodName) && method.ReturnType == ret && method.IsVirtual)
                {
                    return Patcher.GetValidationRules(method.GetCustomAttributes(true));
                }
            }

            return null;
        }
    }
}
