﻿using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Logging;
using Oxide.CSharp;
using Oxide.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Oxide.CSharp.Common;

namespace Oxide.Plugins
{
    public class CSharpPluginLoader : PluginLoader
    {
        public static string[] DefaultReferences = { "mscorlib", "Oxide.Core", "Oxide.CSharp", "Oxide.Common", "System", "System.Core", "System.Data", "System.Xml" };
        public static HashSet<string> PluginReferences = new HashSet<string>(DefaultReferences);
        public static CSharpPluginLoader Instance;

        private static CSharpExtension extension;
        private static Dictionary<string, CompilablePlugin> plugins = new Dictionary<string, CompilablePlugin>();
        private static readonly string[] AssemblyBlacklist = { "Newtonsoft.Json", "protobuf-net", "websocket-sharp" };
        private Core.Libraries.Timer timer { get; } = Interface.Oxide.GetLibrary<Core.Libraries.Timer>();

        public static CompilablePlugin GetCompilablePlugin(string directory, string name)
        {
            string className = Regex.Replace(name, "_", "");
            if (!plugins.TryGetValue(className, out CompilablePlugin plugin))
            {
                plugin = new CompilablePlugin(extension, Instance, directory, name);
                plugins[className] = plugin;
            }
            return plugin;
        }

        public override string FileExtension => ".cs";

        private readonly List<CompilablePlugin> _compilationQueue = new List<CompilablePlugin>();
        private readonly CompilerService _compiler;

        public CSharpPluginLoader(CSharpExtension extension)
        {
            Instance = this;
            CSharpPluginLoader.extension = extension;
            _compiler = new CompilerService(extension);
        }

        public void OnModLoaded()
        {
            _compiler.Precheck();

            // Include references to all loaded game extensions and any assemblies they reference
            foreach (Core.Extensions.Extension extension in Interface.Oxide.GetAllExtensions())
            {
                if (extension == null || !extension.IsCoreExtension && !extension.IsGameExtension)
                {
                    continue;
                }

                System.Reflection.Assembly assembly = extension.GetType().Assembly;
                string assemblyName = assembly.GetName().Name;

                if (AssemblyBlacklist.Contains(assemblyName))
                {
                    continue;
                }

                PluginReferences.Add(assemblyName);
                foreach (System.Reflection.AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference != null)
                    {
                        PluginReferences.Add(reference.Name);
                    }
                }
            }
        }

        public override IEnumerable<string> ScanDirectory(string directory)
        {
            bool installed = _compiler.Installed;
            if (!installed)
            {
                yield break;
            }

            IEnumerable<string> enumerable = base.ScanDirectory(directory);
            foreach (string file in enumerable)
            {
                yield return file;
            }
        }

        /// <summary>
        /// Attempt to asynchronously compile and load plugin
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public override Plugin Load(string directory, string name)
        {
            CompilablePlugin compilablePlugin = GetCompilablePlugin(directory, name);
            if (compilablePlugin.IsLoading)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Warning, LogEvent.Compile, "CSharp", $"Load requested for plugin which is already loading: {compilablePlugin.Name}");
                return null;
            }

            if (LoadedPlugins.ContainsKey(compilablePlugin.Name))
            {
                // Attempt to compile the plugin before unloading the old version
                timer.Once(0.5f, () => Load(compilablePlugin));
            }
            else
            {
                Load(compilablePlugin);
            }

            return null;
        }

        /// <summary>
        /// Attempt to asynchronously compile plugin and only reload if successful
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="name"></param>
        public override void Reload(string directory, string name)
        {
            if (Constants.IncludeRegex.Match(directory).Success)
            {
                name = $"Oxide.{name}";
                foreach (CompilablePlugin plugin in plugins.Values)
                {
                    if (!plugin.References.Contains(name))
                    {
                        continue;
                    }

                    Interface.Oxide.LogInfo($"Reloading {plugin.Name} because it references updated include file: {name}");
                    plugin.LastModifiedAt = DateTime.Now;
                    Load(plugin);
                }
                return;
            }

            CompilablePlugin compilablePlugin = GetCompilablePlugin(directory, name);
            if (compilablePlugin.IsLoading)
            {
                Interface.Oxide.RootLogger.WriteDebug(LogType.Warning, LogEvent.Compile, "CSharp", $"Reload requested for plugin which is already loading: {compilablePlugin.Name}");
                return;
            }

            // Attempt to compile the plugin before unloading the old version
            Load(compilablePlugin);
        }

        /// <summary>
        /// Called when the plugin manager is unloading a plugin that was loaded by this plugin loader
        /// </summary>
        /// <param name="pluginBase"></param>
        public override void Unloading(Plugin pluginBase)
        {
            CSharpPlugin plugin = pluginBase as CSharpPlugin;
            if (plugin == null)
            {
                return;
            }

            LoadedPlugins.Remove(plugin.Name);

            // Unload plugins which require this plugin first
            foreach (CompilablePlugin compilablePlugin in plugins.Values)
            {
                if (compilablePlugin.Requires.Contains(plugin.Name))
                {
                    Interface.Oxide.UnloadPlugin(compilablePlugin.Name);
                }
            }
        }

        public void Load(CompilablePlugin plugin)
        {
            PluginLoadingStarted(plugin);

            plugin.Compile(compiled =>
            {
                if (!compiled)
                {
                    PluginLoadingCompleted(plugin);
                    return;
                }

                IEnumerable<string> loadedLoadingRequirements = plugin.Requires.Where(r => LoadedPlugins.ContainsKey(r) && LoadingPlugins.Contains(r));
                foreach (string loadedPlugin in loadedLoadingRequirements)
                {
                    Interface.Oxide.UnloadPlugin(loadedPlugin);
                }

                IEnumerable<string> missingRequirements = plugin.Requires.Where(r => !LoadedPlugins.ContainsKey(r));
                if (missingRequirements.Any())
                {
                    IEnumerable<string> loadingRequirements = plugin.Requires.Where(r => LoadingPlugins.Contains(r));
                    if (loadingRequirements.Any())
                    {
                        Interface.Oxide.RootLogger.WriteDebug(LogType.Info, LogEvent.Compile, "CSharp",
                            $"{plugin.Name} plugin is waiting for requirements to be loaded: {loadingRequirements.ToSentence()}");
                    }
                    else
                    {
                        string sentence = missingRequirements.ToSentence();
                        Interface.Oxide.LogError($"{plugin.Name} plugin requires missing dependencies: {sentence}");
                        PluginErrors[plugin.Name] = $"Missing dependencies: {sentence}";
                        PluginLoadingCompleted(plugin);
                    }
                }
                else
                {
                    Interface.Oxide.UnloadPlugin(plugin.Name);
                    plugin.LoadPlugin(pl =>
                    {
                        if (pl != null)
                        {
                            LoadedPlugins[pl.Name] = pl;
                        }

                        PluginLoadingCompleted(plugin);
                    });
                }
            });
        }

        /// <summary>
        /// Called when a CompilablePlugin wants to be compiled
        /// </summary>
        /// <param name="plugin"></param>
        public void CompilationRequested(CompilablePlugin plugin)
        {
            if (Compilation.Current != null)
            {
                //Interface.Oxide.LogDebug("Adding plugin to outstanding compilation: {0}", plugin.Name);
                Compilation.Current.Add(plugin);
                return;
            }
            if (_compilationQueue.Count < 1)
            {
                Interface.Oxide.NextTick(() =>
                {
                    CompileAssembly(_compilationQueue.ToArray());
                    _compilationQueue.Clear();
                });
            }
            _compilationQueue.Add(plugin);
        }

        public void PluginLoadingStarted(CompilablePlugin plugin)
        {
            // Let the Oxide core know that this plugin will be loading asynchronously
            if (!LoadingPlugins.Contains(plugin.Name))
            {
                LoadingPlugins.Add(plugin.Name);
            }

            plugin.IsLoading = true;
        }

        private void PluginLoadingCompleted(CompilablePlugin plugin)
        {
            LoadingPlugins.Remove(plugin.Name);
            plugin.IsLoading = false;

            foreach (string loadingName in LoadingPlugins.ToArray())
            {
                CompilablePlugin loadingPlugin = GetCompilablePlugin(plugin.Directory, loadingName);
                if (loadingPlugin.IsLoading && loadingPlugin.Requires.Contains(plugin.Name))
                {
                    Load(loadingPlugin);
                }
            }
        }

        private void CompileAssembly(CompilablePlugin[] plugins)
        {
            _compiler.Compile(plugins, compilation =>
            {
                if (compilation.compiledAssembly == null)
                {
                    foreach (CompilablePlugin plugin in compilation.plugins)
                    {
                        plugin.OnCompilationFailed();
                        PluginErrors[plugin.Name] = $"Failed to compile: {plugin.CompilerErrors}";
                        Interface.Oxide.LogError($"Error while compiling {plugin.ScriptName}: {plugin.CompilerErrors}");
                    }
                }
                else
                {
                    if (compilation.plugins.Count > 0)
                    {
                        string[] compiledNames = compilation.plugins.Where(pl => string.IsNullOrEmpty(pl.CompilerErrors)).Select(pl => pl.Name).ToArray();
                        string verb = compiledNames.Length > 1 ? "were" : "was";
                        Interface.Oxide.LogInfo($"{compiledNames.ToSentence()} {verb} compiled successfully in {Math.Round(compilation.duration * 1000f)}ms");
                    }

                    foreach (CompilablePlugin plugin in compilation.plugins)
                    {
                        if (plugin.CompilerErrors == null)
                        {
                            Interface.Oxide.UnloadPlugin(plugin.Name);
                            plugin.OnCompilationSucceeded(compilation.compiledAssembly);
                        }
                        else
                        {
                            plugin.OnCompilationFailed();
                            PluginErrors[plugin.Name] = $"Failed to compile: {plugin.CompilerErrors}";
                            Interface.Oxide.LogError($"Error while compiling {plugin.ScriptName}: {plugin.CompilerErrors}");
                        }
                    }
                }
            });
        }

        public void OnShutdown() => _compiler.Stop(true, "framework shutting down");
    }
}
