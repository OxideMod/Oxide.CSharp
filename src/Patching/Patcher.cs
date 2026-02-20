extern alias References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;

namespace Oxide.CSharp.Patching
{
    public static class Patcher
    {
        private static Type PatchType { get; } = typeof(IPatch);
        private static Type PatchValidationType { get; } = typeof(PatchValidationAttribute);

        private static Dictionary<Type, List<PatchValidationAttribute>> Patches;

        private static void GetPatches(Assembly module, ref Dictionary<Type, List<PatchValidationAttribute>> patchTypes)
        {
            try
            {
                Type[] types = module.GetTypes();
                int typeCount = types.Length;
                for (int i = 0; i < typeCount; i++)
                {
                    Type type = types[i];

                    if (type.IsAbstract || !PatchType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    List<PatchValidationAttribute> validators = GetValidationRules(type.GetCustomAttributes(PatchValidationType, true)
                        .Concat(type.Assembly.GetCustomAttributes(PatchValidationType, true)).ToArray());

                    patchTypes.Add(type, validators);
                    Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"Found {validators.Count} total validators for patch {type.Name}");
                }
            }
            catch (Exception e)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Error, Logging.LogEvent.Patch, "Patcher", $"Failed to read {module.GetName()?.Name ?? module.FullName} for patches", e);
            }
        }

        private static void GetPatches(Assembly[] modules, ref Dictionary<Type, List<PatchValidationAttribute>> patchTypes)
        {
            int moduleCount = modules.Length;
            for (int i = 0; i < moduleCount; i++)
            {
                GetPatches(modules[i], ref patchTypes);
            }
        }

        public static bool Run(AssemblyDefinition module)
        {
            if (Patches == null)
            {
                Patches = new Dictionary<Type, List<PatchValidationAttribute>>();
                GetPatches(AppDomain.CurrentDomain.GetAssemblies(), ref Patches);
                Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"Found {Patches.Count} patches");
            }

            PatchContext context = new PatchContext(module);
            foreach (KeyValuePair<Type, List<PatchValidationAttribute>> kv in Patches)
            {

                Type patchType = kv.Key;
                List<PatchValidationAttribute> validators = kv.Value;
                context.PatchValidators = validators;
                bool failed = false;

                int validatorCount = validators.Count;
                for (int i = 0; i < validatorCount; i++)
                {
                    PatchValidationAttribute patchValidationAttribute = validators[i];
                    bool pass = patchValidationAttribute.Validate(module);
                    // Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"Validation {valid.GetType().Name}: {(pass ? "passed" : "failed")}");
                    if (pass)
                    {
                        continue;
                    }

                    failed = true;
                    break;
                }

                if (failed)
                {
                    //Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"Validation failed, skipping. . .");
                    continue;
                }

                try
                {
                    IPatch patch = (IPatch)Activator.CreateInstance(patchType, true);
                    context.ContextPatches = 0;
                    patch.Patch(context);
                    Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher",
                        $"{patchType.Name} has applied {context.ContextPatches} patches to {module.Name?.Name ?? module.FullName}");
                }
                catch (Exception exception)
                {
                    Interface.Oxide.RootLogger.WriteDebug(LogType.Error, Logging.LogEvent.Patch, "Patcher",
                        $"{patchType.Name} has applied {context.ContextPatches} patches to {module.Name?.Name ?? module.FullName} but threw a error", exception);
                }
            }

            return context.TotalPatches > 0;
        }

        public static byte[] Run(byte[] data, out bool patched)
        {
            try
            {
                using MemoryStream inStream = new(data);
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(inStream);

                if (Run(assembly))
                {
                    using MemoryStream outStream = new();
                    assembly.Write(outStream);
                    patched = true;
                    return outStream.ToArray();
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Error, Logging.LogEvent.Patch, "Patcher", $"Failed to patch", exception);
            }

            patched = false;
            return data;
        }

        public static List<PatchValidationAttribute> GetValidationRules(object[] attributes)
        {
            List<PatchValidationAttribute> validators = new();

            int attributeCount = attributes.Length;
            for (int i = 0; i < attributeCount; i++)
            {
                if (attributes[i] is not PatchValidationAttribute patchValidationAttribute)
                {
                    continue;
                }

                validators.Add(patchValidationAttribute);
            }

            return validators;
        }
    }
}
