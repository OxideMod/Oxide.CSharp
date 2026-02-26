extern alias References;
using System;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.CSharp.Common;
using References::Cysharp.Text;

namespace Oxide.Plugins
{
    /// <summary>
    /// Indicates that the specified method should be a handler for a covalence command
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CommandAttribute : Attribute
    {
        public string[] Commands { get; }

        public CommandAttribute(params string[] commands)
        {
            Commands = commands;
        }
    }

    /// <summary>
    /// Indicates that the specified method requires a specific permission
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class PermissionAttribute : Attribute
    {
        public string[] Permission { get; }

        public PermissionAttribute(string permission)
        {
            Permission = new[] { permission };
        }
    }

    public class CovalencePlugin : CSharpPlugin
    {
        private static new readonly Covalence covalence = Interface.Oxide.GetLibrary<Covalence>();

        protected string game = covalence.Game;
        protected IPlayerManager players = covalence.Players;
        protected IServer server = covalence.Server;

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        protected void Log(string format, params object[] args) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, args.Length > 0 ? string.Format(format, args) : format);

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="value"></param>
        protected void Log(string value) => Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, value);

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg"></param>
        /// <typeparam name="T"></typeparam>
        protected void Log<T>(string format, T arg) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        protected void Log<T1, T2>(string format, T1 arg1, T2 arg2) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        protected void Log<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        protected void Log<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        protected void Log<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        protected void Log<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <param name="arg7"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        /// <typeparam name="T7"></typeparam>
        protected void Log<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) =>
            Interface.Oxide.LogInfo(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7));

        /// <summary>
        /// Print an info message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <param name="arg7"></param>
        /// <param name="arg8"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        /// <typeparam name="T7"></typeparam>
        /// <typeparam name="T8"></typeparam>
        protected void Log<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            T5 arg5, T6 arg6, T7 arg7, T8 arg8) => Interface.Oxide.LogInfo(Constants.LoggerFormat, Title,
                ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        protected void LogWarning(string format, params object[] args) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, args.Length > 0 ? string.Format(format, args) : format);

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="value"></param>
        protected void LogWarning(string value) => Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, value);

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg"></param>
        /// <typeparam name="T"></typeparam>
        protected void LogWarning<T>(string format, T arg) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        protected void LogWarning<T1, T2>(string format, T1 arg1, T2 arg2) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        protected void LogWarning<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        protected void LogWarning<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        protected void LogWarning<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        protected void LogWarning<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <param name="arg7"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        /// <typeparam name="T7"></typeparam>
        protected void LogWarning<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) =>
            Interface.Oxide.LogWarning(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7));

        /// <summary>
        /// Print a warning message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <param name="arg7"></param>
        /// <param name="arg8"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        /// <typeparam name="T7"></typeparam>
        /// <typeparam name="T8"></typeparam>
        protected void LogWarning<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            T5 arg5, T6 arg6, T7 arg7, T8 arg8) => Interface.Oxide.LogWarning(Constants.LoggerFormat, Title,
                ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        protected void LogError(string format, params object[] args) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, args.Length > 0 ? string.Format(format, args) : format);

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="value"></param>
        protected void LogError(string value) => Interface.Oxide.LogError(Constants.LoggerFormat, Title, value);

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg"></param>
        /// <typeparam name="T"></typeparam>
        protected void LogError<T>(string format, T arg) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        protected void LogError<T1, T2>(string format, T1 arg1, T2 arg2) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        protected void LogError<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        protected void LogError<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        protected void LogError<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        protected void LogError<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <param name="arg7"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        /// <typeparam name="T7"></typeparam>
        protected void LogError<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) =>
            Interface.Oxide.LogError(Constants.LoggerFormat, Title, ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7));

        /// <summary>
        /// Print an error message using the oxide root logger
        /// </summary>
        /// <param name="format"></param>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <param name="arg4"></param>
        /// <param name="arg5"></param>
        /// <param name="arg6"></param>
        /// <param name="arg7"></param>
        /// <param name="arg8"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="T5"></typeparam>
        /// <typeparam name="T6"></typeparam>
        /// <typeparam name="T7"></typeparam>
        /// <typeparam name="T8"></typeparam>
        protected void LogError<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            T5 arg5, T6 arg6, T7 arg7, T8 arg8) => Interface.Oxide.LogError(Constants.LoggerFormat, Title,
                ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8));

        /// <summary>
        /// Called when this plugin has been added to the specified manager
        /// </summary>
        /// <param name="manager"></param>
        public override void HandleAddedToManager(PluginManager manager)
        {
            foreach (MethodInfo method in GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object[] commandAttribute = method.GetCustomAttributes(typeof(CommandAttribute), true);
                object[] permissionAttribute = method.GetCustomAttributes(typeof(PermissionAttribute), true);
                if (commandAttribute.Length <= 0)
                {
                    continue;
                }

                CommandAttribute cmd = commandAttribute[0] as CommandAttribute;
                PermissionAttribute perm = permissionAttribute.Length <= 0 ? null : permissionAttribute[0] as PermissionAttribute;
                if (cmd == null)
                {
                    continue;
                }

                AddCovalenceCommand(cmd.Commands, perm?.Permission, (caller, command, args) =>
                {
                    CallHook(method.Name, caller, command, args);
                    return true;
                });
            }

            base.HandleAddedToManager(manager);
        }
    }
}
