extern alias References;

using Oxide.Core;
using Oxide.Core.CSharp;
using References::Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Oxide.Plugins
{
    public class CompiledAssembly
    {
        public CompilablePlugin[] CompilablePlugins;
        public string[] PluginNames;
        public string Name;
        public DateTime CompiledAt;
        public byte[] RawAssembly;
        public byte[] Symbols;
        public float Duration;
        public Assembly LoadedAssembly;
        public bool IsLoading;
        public bool IsBatch => CompilablePlugins.Length > 1;

        private List<Action<bool>> loadCallbacks = new List<Action<bool>>();
        private bool isPatching;
        private bool isLoaded;

        public CompiledAssembly(string name, CompilablePlugin[] plugins, byte[] rawAssembly, float duration, byte[] symbols)
        {
            Name = name;
            CompilablePlugins = plugins;
            RawAssembly = rawAssembly;
            Duration = duration;
            PluginNames = CompilablePlugins.Select(pl => pl.Name).ToArray();
            Symbols = symbols;
        }

        public void LoadAssembly(Action<bool> callback)
        {
            if (isLoaded)
            {
                callback(true);
                return;
            }

            IsLoading = true;
            loadCallbacks.Add(callback);
            if (isPatching)
            {
                return;
            }

            ValidateAssembly(rawAssembly =>
            {
                if (rawAssembly == null)
                {
                    foreach (Action<bool> cb in loadCallbacks)
                    {
                        cb(true);
                    }

                    loadCallbacks.Clear();
                    IsLoading = false;
                    return;
                }

                LoadedAssembly = Symbols != null ? Assembly.Load(rawAssembly, Symbols) : Assembly.Load(rawAssembly);
                isLoaded = true;

                foreach (Action<bool> cb in loadCallbacks)
                {
                    cb(true);
                }

                loadCallbacks.Clear();

                IsLoading = false;
            });
        }

        private void ValidateAssembly(Action<byte[]> callback)
        {
            if (isPatching)
            {
                Interface.Oxide.LogWarning("Already patching plugin assembly: {0} (ignoring)", PluginNames.ToSentence());
                return;
            }

            isPatching = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AssemblyDefinition definition;
                    using (MemoryStream stream = new MemoryStream(RawAssembly))
                    {
                        definition = AssemblyDefinition.ReadAssembly(stream);
                    }

                    foreach (TypeDefinition type in definition.MainModule.Types)
                    {
                        if (IsCompilerGenerated(type))
                        {
                            continue;
                        }

                        if (type.Namespace == "Oxide.Plugins")
                        {
                            if (PluginNames.Contains(type.Name))
                            {
                                MethodDefinition constructor =
                                    type.Methods.FirstOrDefault(
                                        m => !m.IsStatic && m.IsConstructor && !m.HasParameters && !m.IsPublic);
                                if (constructor != null)
                                {
                                    CompilablePlugin plugin = CompilablePlugins.SingleOrDefault(p => p.Name == type.Name);
                                    if (plugin != null)
                                    {
                                        plugin.CompilerErrors = "Primary constructor in main class must be public";
                                    }
                                }
                                else
                                {
                                    new DirectCallMethod(definition.MainModule, type);
                                }
                            }
                            else
                            {
                                Interface.Oxide.LogWarning(PluginNames.Length == 1
                                                               ? $"{PluginNames[0]} has polluted the global namespace by defining {type.Name}"
                                                               : $"A plugin has polluted the global namespace by defining {type.Name}");
                            }
                        }
                        else if (type.FullName != "<Module>")
                        {
                            if (!PluginNames.Any(plugin => type.FullName.StartsWith($"Oxide.Plugins.{plugin}")))
                            {
                                Interface.Oxide.LogWarning(PluginNames.Length == 1
                                                               ? $"{PluginNames[0]} has polluted the global namespace by defining {type.FullName}"
                                                               : $"A plugin has polluted the global namespace by defining {type.FullName}");
                            }
                        }
                    }

                    Interface.Oxide.NextTick(() =>
                    {
                        isPatching = false;
                        callback(RawAssembly);
                    });
                }
                catch (Exception ex)
                {
                    Interface.Oxide.NextTick(() =>
                    {
                        isPatching = false;
                        Interface.Oxide.LogException($"Exception while patching: {PluginNames.ToSentence()}", ex);
                        callback(null);
                    });
                }
            });
        }

        public bool IsOutdated() => CompilablePlugins.Any(pl => pl.GetLastModificationTime() != CompiledAt);

        private bool IsCompilerGenerated(TypeDefinition type) => type.CustomAttributes.Any(attr => attr.Constructor.DeclaringType.ToString().Contains("CompilerGeneratedAttribute"));
    }
}
