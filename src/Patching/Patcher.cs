extern alias References;

using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];

                    if (!type.IsAbstract && PatchType.IsAssignableFrom(type))
                    {
                        List<PatchValidationAttribute> validators = GetValidationRules(type.GetCustomAttributes(PatchValidationType, true).Concat(type.Assembly.GetCustomAttributes(PatchValidationType, true)).ToArray());
                        patchTypes.Add(type, validators);
                        Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"Found {validators.Count} total validators for patch {type.Name}");
                    }
                }
            }
            catch (Exception e)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Error, Logging.LogEvent.Patch, "Patcher", $"Failed to read {module.GetName()?.Name ?? module.FullName} for patches", e);
            }
        }

        private static void GetPatches(Assembly[] modules, ref Dictionary<Type, List<PatchValidationAttribute>> patchTypes)
        {
            for (int i = 0; i < modules.Length; i++)
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
            foreach (var kv in Patches)
            {

                Type patchType = kv.Key;
                List<PatchValidationAttribute> validators = kv.Value;
                context.PatchValidators = validators;
                bool failed = false;
                for (int n = 0; n < validators.Count; n++)
                {
                    PatchValidationAttribute valid = validators[n];
                    bool pass = valid.IsValid(module);
                    // Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"Validation {valid.GetType().Name}: {(pass ? "passed" : "failed")}");
                    if (!pass)
                    {
                        failed = true;
                        break;
                    }
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
                    Interface.Oxide.RootLogger.WriteDebug(LogType.Info, Logging.LogEvent.Patch, "Patcher", $"{patchType.Name} has applied {context.ContextPatches} patches to {module.Name?.Name ?? module.FullName}");
                }
                catch (Exception e)
                {
                    Interface.Oxide.RootLogger.WriteDebug(LogType.Error, Logging.LogEvent.Patch, "Patcher", $"{patchType.Name} has applied {context.ContextPatches} patches to {module.Name?.Name ?? module.FullName} but threw a error", e);
                }
            }

            return context.TotalPatches > 0;
        }
        
        public static byte[] Run(byte[] data, out bool patched)
        {
            try
            {
                using (MemoryStream inStream = new MemoryStream(data))
                {
                    AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(inStream);

                    if (Run(assembly))
                    {
                        using (MemoryStream outStream = new MemoryStream())
                        {
                            assembly.Write(outStream);
                            patched = true;
                            return outStream.ToArray();
                        }
                    }
                }
                    
            }
            catch (Exception e)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Error, Logging.LogEvent.Patch, "Patcher", $"Failed to patch", e);
            }

            patched = false;
            return data;
        }

        public static List<PatchValidationAttribute> GetValidationRules(object[] attributes)
        {
            List<PatchValidationAttribute> validators = new List<PatchValidationAttribute>();

            for (int i = 0; i < attributes.Length; i++)
            {
                Attribute a = attributes[i] as Attribute;
                if (a is PatchValidationAttribute valid)
                {
                    validators.Add(valid);
                }
            }

            return validators;
        }
    }
}
