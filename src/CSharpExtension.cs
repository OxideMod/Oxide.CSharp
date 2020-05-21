using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Plugins.Watchers;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oxide.Plugins
{
    /// <summary>
    /// The extension class that represents this extension
    /// </summary>
    public class CSharpExtension : Extension
    {
        internal static Assembly Assembly = Assembly.GetExecutingAssembly();
        internal static AssemblyName AssemblyName = Assembly.GetName();
        internal static VersionNumber AssemblyVersion = new VersionNumber(AssemblyName.Version.Major, AssemblyName.Version.Minor, AssemblyName.Version.Build);
        internal static string AssemblyAuthors = ((AssemblyCompanyAttribute)Attribute.GetCustomAttribute(Assembly, typeof(AssemblyCompanyAttribute), false)).Company;

        /// <summary>
        /// Gets whether this extension is a core extension
        /// </summary>
        public override bool IsCoreExtension => true;

        /// <summary>
        /// Gets the name of this extension
        /// </summary>
        public override string Name => "CSharp";

        /// <summary>
        /// Gets the author of this extension
        /// </summary>
        public override string Author => AssemblyAuthors;

        /// <summary>
        /// Gets the version of this extension
        /// </summary>
        public override VersionNumber Version => AssemblyVersion;

        public FSWatcher Watcher { get; private set; }

        // The .cs plugin loader
        private CSharpPluginLoader loader;

        // Is the sandbox enabled? (always default to true)
        public static bool SandboxEnabled { get; private set; } = true;

        /// <summary>
        /// Initializes a new instance of the CSharpExtension class
        /// </summary>
        /// <param name="manager"></param>
        public CSharpExtension(ExtensionManager manager) : base(manager)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                string extDir = Interface.Oxide.ExtensionDirectory;
                string configPath = Path.Combine(extDir, "Oxide.References.dll.config");
                if (!File.Exists(configPath) || (new[] { "target=\"x64", "target=\"./x64" }.Any(File.ReadAllText(configPath).Contains)))
                {
                    File.WriteAllText(configPath, $"<configuration>\n<dllmap dll=\"MonoPosixHelper\" target=\"{extDir}/x86/libMonoPosixHelper.so\" os=\"!windows,osx\" wordsize=\"32\" />\n" +
                        $"<dllmap dll=\"MonoPosixHelper\" target=\"{extDir}/x64/libMonoPosixHelper.so\" os=\"!windows,osx\" wordsize=\"64\" />\n</configuration>");
                }
            }
        }

        /// <summary>
        /// Loads this extension
        /// </summary>
        public override void Load()
        {
            // Register our loader
            loader = new CSharpPluginLoader(this);
            Manager.RegisterPluginLoader(loader);

            // Register engine frame callback
            Interface.Oxide.OnFrame(OnFrame);

            if (File.Exists(Path.Combine(Interface.Oxide.ExtensionDirectory, "oxide.disable-sandbox")))
            {
                Interface.Oxide.LogWarning($"{Path.Combine(Interface.Oxide.ExtensionDirectory, "oxide.disable-sandbox")} found, disabling security sandbox.  Potentially dangerous APIs and methods are now allowed inside plugins.");
                CSharpExtension.SandboxEnabled = false;
            }
        }

        /// <summary>
        /// Loads plugin watchers used by this extension
        /// </summary>
        /// <param name="pluginDirectory"></param>
        public override void LoadPluginWatchers(string pluginDirectory)
        {
            // Register the watcher
            Watcher = new FSWatcher(pluginDirectory, "*.cs");
            Manager.RegisterPluginChangeWatcher(Watcher);
        }

        /// <summary>
        /// Called when all other extensions have been loaded
        /// </summary>
        public override void OnModLoad() => loader.OnModLoaded();

        public override void OnShutdown()
        {
            base.OnShutdown();
            loader.OnShutdown();
        }

        /// <summary>
        /// Called by engine every server frame
        /// </summary>
        private void OnFrame(float delta)
        {
            object[] args = new object[] { delta };
            foreach (System.Collections.Generic.KeyValuePair<string, Core.Plugins.Plugin> kv in loader.LoadedPlugins)
            {
                CSharpPlugin plugin = kv.Value as CSharpPlugin;
                if (plugin != null && plugin.HookedOnFrame)
                {
                    plugin.CallHook("OnFrame", args);
                }
            }
        }
    }
}
