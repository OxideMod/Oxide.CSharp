using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core.Plugins.Watchers;
using Oxide.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Oxide.Plugins
{
    public class PluginLoadFailure : Exception
    {
        public PluginLoadFailure(string reason)
        {
        }
    }

    /// <summary>
    /// Allows configuration of plugin info using an attribute above the plugin class
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class InfoAttribute : Attribute
    {
        public string Title { get; }
        public string Author { get; }
        public VersionNumber Version { get; private set; }
        public int ResourceId { get; set; }

        public InfoAttribute(string Title, string Author, string Version)
        {
            this.Title = Title;
            this.Author = Author;
            SetVersion(Version);
        }

        public InfoAttribute(string Title, string Author, double Version)
        {
            this.Title = Title;
            this.Author = Author;
            SetVersion(Version.ToString());
        }

        private void SetVersion(string version)
        {
            List<ushort> versionParts = version.Split('.').Select(part =>
            {
                if (!ushort.TryParse(part, out ushort number))
                {
                    number = 0;
                }
                return number;
            }).ToList();

            while (versionParts.Count < 3)
            {
                versionParts.Add(0);
            }

            if (versionParts.Count > 3)
            {
                Interface.Oxide.LogWarning($"Version `{version}` is invalid for {Title}, should be `major.minor.patch`");
            }

            Version = new VersionNumber(versionParts[0], versionParts[1], versionParts[2]);
        }
    }

    /// <summary>
    /// Allows plugins to specify a description of the plugin using an attribute above the plugin class
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; }

        public DescriptionAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// Indicates that the specified field should be a reference to another plugin when it is loaded
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class PluginReferenceAttribute : Attribute
    {
        public string Name { get; }

        public PluginReferenceAttribute()
        {
        }

        public PluginReferenceAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Indicates that the specified method should be a handler for a console command
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ConsoleCommandAttribute : Attribute
    {
        public string Command { get; private set; }

        public ConsoleCommandAttribute(string command)
        {
            Command = command.Contains('.') ? command : ("global." + command);
        }
    }

    /// <summary>
    /// Indicates that the specified method should be a handler for a chat command
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ChatCommandAttribute : Attribute
    {
        public string Command { get; private set; }

        public ChatCommandAttribute(string command)
        {
            Command = command;
        }
    }

    /// <summary>
    /// Indicates that the specified Hash field should be used to automatically track online players
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class OnlinePlayersAttribute : Attribute
    {
    }

    /// <summary>
    /// Base class which all dynamic CSharp plugins must inherit
    /// </summary>
    public abstract class CSharpPlugin : CSPlugin
    {
        /// <summary>
        /// Wrapper for dynamically managed plugin fields
        /// </summary>
        public class PluginFieldInfo
        {
            public Plugin Plugin;
            public FieldInfo Field;
            public Type FieldType;
            public Type[] GenericArguments;
            public Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();

            public PluginFieldInfo(Plugin plugin, FieldInfo field)
            {
                Plugin = plugin;
                Field = field;
                FieldType = field.FieldType;
                GenericArguments = FieldType.GetGenericArguments();
            }

            public bool HasValidConstructor(params Type[] argument_types)
            {
                Type type = GenericArguments[1];
                return type.GetConstructor(new Type[0]) != null || type.GetConstructor(argument_types) != null;
            }

            public object Value => Field.GetValue(Plugin);

            public bool LookupMethod(string method_name, params Type[] argument_types)
            {
                MethodInfo method = FieldType.GetMethod(method_name, argument_types);
                if (method == null)
                {
                    return false;
                }

                Methods[method_name] = method;
                return true;
            }

            public object Call(string method_name, params object[] args)
            {
                if (!Methods.TryGetValue(method_name, out MethodInfo method))
                {
                    method = FieldType.GetMethod(method_name, BindingFlags.Instance | BindingFlags.Public);
                    Methods[method_name] = method;
                }
                if (method == null)
                {
                    throw new MissingMethodException(FieldType.Name, method_name);
                }

                return method.Invoke(Value, args);
            }
        }

        public FSWatcher Watcher;

        protected Covalence covalence = Interface.Oxide.GetLibrary<Covalence>();
        protected Core.Libraries.Lang lang = Interface.Oxide.GetLibrary<Core.Libraries.Lang>();
        protected Core.Libraries.Plugins plugins = Interface.Oxide.GetLibrary<Core.Libraries.Plugins>();
        protected Core.Libraries.Permission permission = Interface.Oxide.GetLibrary<Core.Libraries.Permission>();
        protected Core.Libraries.WebRequests webrequest = Interface.Oxide.GetLibrary<Core.Libraries.WebRequests>();
        protected PluginTimers timer;

        protected HashSet<PluginFieldInfo> onlinePlayerFields = new HashSet<PluginFieldInfo>();
        private Dictionary<string, MemberInfo> pluginReferenceMembers = new Dictionary<string, MemberInfo>();

        private bool hookDispatchFallback;

        public bool HookedOnFrame
        {
            get; private set;
        }

        public CSharpPlugin()
        {
            timer = new PluginTimers(this);

            Type type = GetType();
            foreach (MemberInfo member in type.GetMembers(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (member.MemberType == MemberTypes.Property || member.MemberType == MemberTypes.Field)
                {
                    if (member.MemberType == MemberTypes.Property)
                    {
                        PropertyInfo property = member as PropertyInfo;
                        if (!property.CanWrite)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        FieldInfo field = member as FieldInfo;
                    }

                    object[] reference_attributes = member.GetCustomAttributes(typeof(PluginReferenceAttribute), true);

                    if (reference_attributes.Length > 0)
                    {
                        PluginReferenceAttribute pluginReference = reference_attributes[0] as PluginReferenceAttribute;
                        pluginReferenceMembers[pluginReference.Name ?? member.Name] = member;
                    }
                }
                else if (member.MemberType == MemberTypes.Method)
                {
                    MethodInfo method = member as MethodInfo;
                    object[] info_attributes = method.GetCustomAttributes(typeof(HookMethodAttribute), true);
                    if (info_attributes.Length > 0)
                    {
                        continue;
                    }

                    if (method.Name.Equals("OnFrame"))
                    {
                        HookedOnFrame = true;
                    }
                    // Assume all private instance methods which are not explicitly hooked could be hooks
                    if (method.DeclaringType.Name == type.Name)
                    {
                        AddHookMethod(method.Name, method);
                    }
                }
            }
        }

        public virtual bool SetPluginInfo(string name, string path)
        {
            Name = name;
            Filename = path;

            object[] infoAttributes = GetType().GetCustomAttributes(typeof(InfoAttribute), true);
            if (infoAttributes.Length > 0)
            {
                InfoAttribute info = infoAttributes[0] as InfoAttribute;
                Title = info.Title;
                Author = info.Author;
                Version = info.Version;
                ResourceId = info.ResourceId;
            }
            else
            {
                Interface.Oxide.LogWarning($"Failed to load {name}: Info attribute missing");
                return false;
            }

            object[] descriptionAttributes = GetType().GetCustomAttributes(typeof(DescriptionAttribute), true);
            if (descriptionAttributes.Length > 0)
            {
                DescriptionAttribute info = descriptionAttributes[0] as DescriptionAttribute;
                Description = info.Description;
            }

            MethodInfo config = GetType().GetMethod("LoadDefaultConfig", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            HasConfig = config.DeclaringType != typeof(Plugin);

            MethodInfo messages = GetType().GetMethod("LoadDefaultMessages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            HasMessages = messages.DeclaringType != typeof(Plugin);

            return true;
        }

        public override void HandleAddedToManager(PluginManager manager)
        {
            base.HandleAddedToManager(manager);

            if (Filename != null)
            {
                Watcher.AddMapping(Name);
            }

            Manager.OnPluginAdded += OnPluginLoaded;
            Manager.OnPluginRemoved += OnPluginUnloaded;

            foreach (var member in pluginReferenceMembers)
            {
                if (member.Value.MemberType == MemberTypes.Property)
                {
                    ((PropertyInfo)member.Value).SetValue(this, manager.GetPlugin(member.Key), null);
                }
                else
                {
                    ((FieldInfo)member.Value).SetValue(this, manager.GetPlugin(member.Key));
                }
            }

            /*var compilable_plugin = CSharpPluginLoader.GetCompilablePlugin(Interface.Oxide.PluginDirectory, Name);
            if (compilable_plugin != null && compilable_plugin.CompiledAssembly != null)
            {
                System.IO.File.WriteAllBytes(Interface.Oxide.PluginDirectory + "\\" + Name + ".dump", compilable_plugin.CompiledAssembly.PatchedAssembly);
                Interface.Oxide.LogWarning($"The raw assembly has been dumped to Plugins/{Name}.dump");
            }*/

            try
            {
                OnCallHook("Loaded", null);
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogException($"Failed to initialize plugin '{Name} v{Version}'", ex);
                Loader.PluginErrors[Name] = ex.Message;
            }
        }

        public override void HandleRemovedFromManager(PluginManager manager)
        {
            if (IsLoaded)
            {
                CallHook("Unload", null);
            }

            Watcher.RemoveMapping(Name);

            Manager.OnPluginAdded -= OnPluginLoaded;
            Manager.OnPluginRemoved -= OnPluginUnloaded;

            foreach (var member in pluginReferenceMembers)
            {
                if (member.Value.MemberType == MemberTypes.Property)
                {
                    ((PropertyInfo)member.Value).SetValue(this, null, null);
                }
                else
                {
                    ((FieldInfo)member.Value).SetValue(this, null);
                }
            }

            base.HandleRemovedFromManager(manager);
        }

        public virtual bool DirectCallHook(string name, out object ret, object[] args)
        {
            ret = null;
            return false;
        }

        protected override object InvokeMethod(HookMethod method, object[] args)
        {
            // TODO: Ignore base_ methods for now
            if (!hookDispatchFallback && !method.IsBaseHook)
            {
                if (args != null && args.Length > 0)
                {
                    ParameterInfo[] parameters = method.Parameters;
                    for (int i = 0; i < args.Length; i++)
                    {
                        object value = args[i];
                        if (value == null)
                        {
                            continue;
                        }

                        Type parameter_type = parameters[i].ParameterType;
                        if (!parameter_type.IsValueType)
                        {
                            continue;
                        }

                        Type argument_type = value.GetType();
                        if (parameter_type != typeof(object) && argument_type != parameter_type)
                        {
                            args[i] = Convert.ChangeType(value, parameter_type);
                        }
                    }
                }
                try
                {
                    if (DirectCallHook(method.Name, out object ret, args))
                    {
                        return ret;
                    }

                    Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Error, LogEvent.HookCall, Name, "DirectCallHook method is not patched, falling back to reflection based dispatch.");
                    hookDispatchFallback = true;
                }
                catch (InvalidProgramException ex)
                {
                    Interface.Oxide.LogError("Hook dispatch failure detected, falling back to reflection based dispatch. " + ex);
                    CompilablePlugin compilablePlugin = CSharpPluginLoader.GetCompilablePlugin(Interface.Oxide.PluginDirectory, Name);
                    if (compilablePlugin?.CompiledAssembly != null)
                    {
                        File.WriteAllBytes(Interface.Oxide.PluginDirectory + "\\" + Name + ".dump", compilablePlugin.CompiledAssembly.RawAssembly);
                        Interface.Oxide.LogWarning($"The invalid raw assembly has been dumped to Plugins/{Name}.dump");
                    }
                    hookDispatchFallback = true;
                }
            }

            return method.Method.Invoke(this, args);
        }

        /// <summary>
        /// Called from Init/Loaded callback to set a failure reason and unload the plugin
        /// </summary>
        /// <param name="reason"></param>
        public void SetFailState(string reason)
        {
            throw new PluginLoadFailure(reason);
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (pluginReferenceMembers.TryGetValue(plugin.Name, out MemberInfo member))
            {

                if (member.MemberType == MemberTypes.Property)
                {
                    ((PropertyInfo)member).SetValue(this, plugin, null);
                }
                else
                {
                    ((FieldInfo)member).SetValue(this, plugin);
                }
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (pluginReferenceMembers.TryGetValue(plugin.Name, out MemberInfo member))
            {

                if (member.MemberType == MemberTypes.Property)
                {
                    ((PropertyInfo)member).SetValue(this, null, null);
                }
                else
                {
                    ((FieldInfo)member).SetValue(this, null);
                }
            }
        }

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        protected void Puts(string format, params object[] args)
        {
            Interface.Oxide.LogInfo("[{0}] {1}", Title, args.Length > 0 ? string.Format(format, args) : format);
        }

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        protected void PrintWarning(string format, params object[] args)
        {
            Interface.Oxide.LogWarning("[{0}] {1}", Title, args.Length > 0 ? string.Format(format, args) : format);
        }

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        protected void PrintError(string format, params object[] args)
        {
            Interface.Oxide.LogError("[{0}] {1}", Title, args.Length > 0 ? string.Format(format, args) : format);
        }

        private static readonly object _logFileLock = new object();

        /// <summary>
        /// Logs a string of text to a named file
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="text"></param>
        /// <param name="plugin"></param>
        /// <param name="datedFilename"></param>
        /// <param name="timestampPrefix"></param>
        protected void LogToFile(string filename, string text, Plugin plugin, bool datedFilename = true, bool timestampPrefix = false)
        {
            string path = Path.Combine(Interface.Oxide.LogDirectory, plugin.Name);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            filename = $"{plugin.Name.ToLower()}_{filename.ToLower()}{(datedFilename ? $"-{DateTime.Now:yyyy-MM-dd}" : "")}.txt";

            lock (_logFileLock)
            {
                using (FileStream file = new FileStream(Path.Combine(path, Utility.CleanPath(filename)), FileMode.Append, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(file, Encoding.UTF8))
                {
                    writer.WriteLine(timestampPrefix ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}" : text);
                }
            }
        }

        /// <summary>
        /// Queue a callback to be called in the next server frame
        /// </summary>
        /// <param name="callback"></param>
        protected void NextFrame(Action callback) => Interface.Oxide.NextTick(callback);

        /// <summary>
        /// Queue a callback to be called in the next server frame
        /// </summary>
        /// <param name="callback"></param>
        protected void NextTick(Action callback) => Interface.Oxide.NextTick(callback);

        /// <summary>
        /// Queues a callback to be called from a thread pool worker thread
        /// </summary>
        /// <param name="callback"></param>
        protected void QueueWorkerThread(Action<object> callback)
        {
            ThreadPool.QueueUserWorkItem(context =>
            {
                try
                {
                    callback(context);
                }
                catch (Exception ex)
                {
                    RaiseError($"Exception in '{Name} v{Version}' plugin worker thread: {ex}");
                }
            });
        }
    }
}
