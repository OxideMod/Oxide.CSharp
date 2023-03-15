extern alias References;

using Oxide.Core;
using Oxide.Core.CSharp;
using Oxide.Core.Logging;
using Oxide.CSharp;
using Oxide.Logging;
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
        public byte[] PatchedAssembly;
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

                LoadedAssembly = Assembly.Load(rawAssembly);
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
                Interface.Oxide.RootLogger.WriteDebug(LogType.Warning, Logging.LogEvent.Compile, "CSharp", $"Already patching plugin assembly: {PluginNames.ToSentence()} (ignoring)");
                return;
            }

            isPatching = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AssemblyDefinition definition = null;
                    using (MemoryStream stream = new MemoryStream(RawAssembly))
                    {
                        definition = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters() { AssemblyResolver = new AssemblyResolver() } );
                    }

                    int foundPlugins = 0;
                    int totalPlugins = CompilablePlugins.Count(p => p.CompilerErrors == null);
                    for (int i = 0; i < definition.MainModule.Types.Count; i++)
                    {
                        if (foundPlugins == totalPlugins)
                        {
                            Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp", $"Patched {foundPlugins} of {totalPlugins} plugins");
                            break;
                        }
                        try
                        {
                            TypeDefinition type = definition.MainModule.Types[i];

                            if (type.Namespace != "Oxide.Plugins")
                            {
                                continue;
                            }

                            if (PluginNames.Contains(type.Name))
                            {
                                foundPlugins++;

                                Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp", $"Preparing {type.Name} for runtime patching. . .");

                                MethodDefinition constructor =
                                    type.Methods.FirstOrDefault(
                                        m => !m.IsStatic && m.IsConstructor && !m.HasParameters && !m.IsPublic);

                                if (constructor != null)
                                {
                                    Interface.Oxide.RootLogger.WriteDebug(LogType.Error, LogEvent.Compile, "CSharp", $"User defined constructors are not supported. Please remove the constructor from {type.Name}.cs"); // Should be allowed
                                    CompilablePlugin plugin = CompilablePlugins.SingleOrDefault(p => p.Name == type.Name);
                                    if (plugin != null)
                                    {
                                        plugin.CompilerErrors = "Primary constructor in main class must be public";
                                    }
                                }
                                else
                                {
                                    Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp", $"Patching DirectCallMethod on {type.Name}");
                                    new DirectCallMethod(definition.MainModule, type);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Interface.Oxide.RootLogger.WriteDebug(LogType.Error, LogEvent.Compile, "CSharp", $"Failed to patch type at index {i}", e);
                        }
                    }

                    using (MemoryStream stream = new MemoryStream())
                    {
                        definition.Write(stream);
                        PatchedAssembly = stream.ToArray();
                    }

                    Interface.Oxide.NextTick(() =>
                    {
                        isPatching = false;
                        callback(PatchedAssembly);
                    });
                }
                catch (Exception ex)
                {
                    Interface.Oxide.NextTick(() =>
                    {
                        isPatching = false;
                        Interface.Oxide.RootLogger.WriteDebug(LogType.Warning, LogEvent.Compile, "CSharp", $"Failed to patch DirectCallHook method on plugins {PluginNames.ToSentence()}, performance may be degraded.", ex);
                        callback(RawAssembly);
                    });
                }
            });
        }

        public bool IsOutdated() => CompilablePlugins.Any(pl => pl.GetLastModificationTime() != CompiledAt);
    }
}
