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

        protected List<PatchValidationAttribute> TypeValidators { get; }

        protected List<PatchValidationAttribute> PropertyValidators { get; }

        protected List<PatchValidationAttribute> FieldValidators { get; }

        protected List<PatchValidationAttribute> MethodValidators { get; }

        protected List<PatchValidationAttribute> EventValidators { get; }

        protected List<PatchValidationAttribute> MemberValidators { get; }

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
            int typeCount = types.Count;
            for (int i = 0; i < typeCount; i++)
            {
                TypeDefinition type = types[i];
                RecurseType(type, context);
            }

            OnPatchFinished(context);
        }

        private void RecurseType(TypeDefinition type, PatchContext context)
        {
            if (RunValidation(type, MemberValidators) && OnMemberDefinition(type))
            {
                context.IncrementPatches();
            }

            if (type.HasProperties)
            {
                int propertyCount = type.Properties.Count;
                for (int i = 0; i < propertyCount; i++)
                {
                    PropertyDefinition prop = type.Properties[i];
                    if (!RunValidation(prop, MemberValidators) || !OnMemberDefinition(prop))
                    {
                        continue;
                    }

                    context.IncrementPatches();
                }
            }

            if (type.HasFields)
            {
                int fieldCount = type.Fields.Count;
                for (int i = 0; i < fieldCount; i++)
                {
                    FieldDefinition field = type.Fields[i];
                    if (!RunValidation(field, MemberValidators) || !OnMemberDefinition(field))
                    {
                        continue;
                    }

                    context.IncrementPatches();
                }
            }

            if (type.HasMethods)
            {
                int methodCount = type.Methods.Count;
                for (int i = 0; i < methodCount; i++)
                {
                    MethodDefinition method = type.Methods[i];
                    if (!RunValidation(method, MemberValidators) || !OnMemberDefinition(method))
                    {
                        continue;
                    }

                    context.IncrementPatches();
                }
            }

            if (type.HasEvents)
            {
                int eventCount = type.Events.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    EventDefinition eventDefinition = type.Events[i];
                    if (!RunValidation(eventDefinition, MemberValidators) || !OnMemberDefinition(eventDefinition))
                    {
                        continue;
                    }

                    context.IncrementPatches();
                }
            }

            if (type.HasNestedTypes)
            {
                int nestedTypeCount = type.NestedTypes.Count;
                for (int i = 0; i < nestedTypeCount; i++)
                {
                    RecurseType(type.NestedTypes[i], context);
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
            return member switch
            {
                TypeDefinition type => RunValidation(member, TypeValidators) && OnTypeDefinition(type),
                PropertyDefinition prop => RunValidation(member, PropertyValidators) && OnPropertyDefinition(prop),
                FieldDefinition field => RunValidation(member, FieldValidators) && OnFieldDefinition(field),
                MethodDefinition method => RunValidation(method, MethodValidators) && OnMethodDefinition(method),
                EventDefinition @event => RunValidation(@event, EventValidators) && OnEventDefinition(@event),
                _ => false
            };
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
        /// <param name="eventDefinition">The event</param>
        /// <returns>True if a patch was applied</returns>
        protected virtual bool OnEventDefinition(EventDefinition eventDefinition)
        {
            return false;
        }

        protected virtual void OnPatchFinished(PatchContext context)
        {
        }

        protected bool RunValidation(IMemberDefinition member, List<PatchValidationAttribute> validations)
        {
            if (member == null || validations == null)
            {
                return false;
            }

            int validationCount = validations.Count;
            for (int i = 0; i < validationCount; i++)
            {
                PatchValidationAttribute valid = validations[i];
                if (valid.Validate(member))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        protected void Log(string message, LogType logType = LogType.Info, Exception e = null)
        {
            Interface.Oxide.RootLogger.WriteDebug(logType, Logging.LogEvent.Patch, Name, message, e);
        }

        private static List<PatchValidationAttribute> GetValidationRules(string methodName, Type type)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type ret = typeof(bool);

            int methodCount = methods.Length;
            for (int i = 0; i < methodCount; i++)
            {
                MethodInfo method = methods[i];
                if (!method.Name.Equals(methodName) || method.ReturnType != ret || !method.IsVirtual)
                {
                    continue;
                }

                return Patcher.GetValidationRules(method.GetCustomAttributes(true));
            }

            return null;
        }
    }
}
