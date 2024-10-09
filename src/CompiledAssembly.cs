extern alias References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Oxide.Core;
using Oxide.Core.CSharp;
using Oxide.Core.Logging;
using Oxide.CSharp;
using Oxide.Logging;
using References::Mono.Cecil;
using References::Mono.Cecil.Cil;

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
        public byte[] PatchedSymbols;
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

            ValidateAssembly((assembly, symbols) =>
            {
                if (assembly == null)
                {
                    foreach (Action<bool> loadingCallbacks in loadCallbacks)
                    {
                        loadingCallbacks(true);
                    }

                    loadCallbacks.Clear();
                    IsLoading = false;
                    return;
                }

                LoadedAssembly = Assembly.Load(assembly, symbols);
                isLoaded = true;

                foreach (Action<bool> loadingCallbacks in loadCallbacks)
                {
                    loadingCallbacks(true);
                }

                loadCallbacks.Clear();

                IsLoading = false;
            });
        }

        // TODO: Clean this up
        private void ValidateAssembly(Action<byte[], byte[]> callback)
        {
            if (isPatching)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Warning, LogEvent.Compile, "CSharp",
                    $"Already patching plugin assembly: {PluginNames.ToSentence()} (ignoring)");
                return;
            }

            isPatching = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using AssemblyResolver assemblyResolver = new();
                    ReaderParameters readerParameters = new()
                    {
                        AssemblyResolver = assemblyResolver,
                        ReadSymbols = true,
                        SymbolReaderProvider = new PortablePdbReaderProvider()
                    };

                    AssemblyDefinition baseAssembly = AssemblyDefinition.ReadAssembly(
                        Path.Combine(Interface.Oxide.ExtensionDirectory, "Oxide.CSharp.dll"), new ReaderParameters
                        {
                            AssemblyResolver = assemblyResolver,
                            ReadSymbols = false,
                        });

                    using MemoryStream assemblyStream = new(RawAssembly);
                    using MemoryStream symbolStream = new(Symbols);
                    readerParameters.SymbolStream = symbolStream;

                    AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyStream, readerParameters);

                    int foundPlugins = 0;
                    int totalPlugins = CompilablePlugins.Count(p => p.CompilerErrors == null);
                    for (int i = 0; i < assemblyDefinition.MainModule.Types.Count; i++)
                    {
                        if (foundPlugins == totalPlugins)
                        {
                            Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp",
                                $"Patched {foundPlugins} of {totalPlugins} plugins");
                            break;
                        }
                        try
                        {
                            TypeDefinition typeDefinition = assemblyDefinition.MainModule.Types[i];

                            if (typeDefinition.Namespace != "Oxide.Plugins")
                            {
                                continue;
                            }

                            if (PluginNames.Contains(typeDefinition.Name))
                            {
                                foundPlugins++;

                                Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp",
                                    $"Preparing {typeDefinition.Name} for runtime patching. . .");

                                MethodDefinition constructor =
                                    typeDefinition.Methods.FirstOrDefault(
                                        m => !m.IsStatic && m.IsConstructor && !m.HasParameters && !m.IsPublic);

                                if (constructor != null)
                                {
                                    Interface.Oxide.RootLogger.WriteDebug(LogType.Error, LogEvent.Compile, "CSharp",
                                        $"User defined constructors are not supported. Please remove the constructor from {typeDefinition.Name}.cs"); // Should be allowed

                                    CompilablePlugin plugin = CompilablePlugins.SingleOrDefault(p => p.Name == typeDefinition.Name);
                                    if (plugin != null)
                                    {
                                        plugin.CompilerErrors = "Primary constructor in main class must be public";
                                    }
                                }
                                else
                                {
                                    Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp", $"Patching DirectCallMethod on {typeDefinition.Name}");
                                    new DirectCallMethod(assemblyDefinition.MainModule, typeDefinition, baseAssembly);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Interface.Oxide.RootLogger.WriteDebug(LogType.Error, LogEvent.Compile, "CSharp", $"Failed to patch type at index {i}", e);
                        }
                    }

                    using MemoryStream writeAssemblyStream = new();
                    using MemoryStream writeSymbolStream = new();

                    assemblyDefinition.Write(writeAssemblyStream, new WriterParameters
                    {
                        WriteSymbols = true,
                        SymbolStream = writeSymbolStream,
                        SymbolWriterProvider = new PortablePdbWriterProvider()
                    });

                    PatchedAssembly = writeAssemblyStream.ToArray();
                    PatchedSymbols = writeSymbolStream.ToArray();

                    Interface.Oxide.NextTick(() =>
                    {
                        isPatching = false;
                        callback(PatchedAssembly, PatchedSymbols);
                    });
                }
                catch (Exception ex)
                {
                    Interface.Oxide.NextTick(() =>
                    {
                        isPatching = false;
                        Interface.Oxide.RootLogger.WriteDebug(LogType.Warning, LogEvent.Compile, "CSharp",
                            $"Failed to patch DirectCallHook method on plugins {PluginNames.ToSentence()}, performance may be degraded.", ex);
                        callback(RawAssembly, Symbols);
                    });
                }
            });
        }

        public bool IsOutdated() => CompilablePlugins.Any(pl => pl.GetLastModificationTime() != CompiledAt);
    }
}
