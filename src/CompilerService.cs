extern alias References;

using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.Logging;
using Oxide.Plugins;
using Oxide.Pooling;
using References::Mono.Unix.Native;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Oxide.Core.Extensions;
using Oxide.CSharp.Common;
using Oxide.CSharp.CompilerStream;
using CompilerMessage = Oxide.CSharp.CompilerStream.CompilerMessage;

namespace Oxide.CSharp
{
    internal class CompilerService
    {
        private readonly Hash<int, Compilation> _compilations;
        private readonly Queue<CompilerMessage> _messageQueue = new Queue<CompilerMessage>();
        private Process? _compilerProcess;
        private volatile int _lastId;
        private volatile bool _ready;
        private Core.Libraries.Timer.TimerInstance _idleTimer;
        private MessageBrokerService? _messageBrokerService;
        private readonly string _filePath;
        private string _remoteName;
        private float _startTime;
        private string[] _preprocessor;
        private readonly string _pipeName;

        public bool Installed => File.Exists(_filePath);

        public CompilerService(Extension extension)
        {
            _compilations = new Hash<int, Compilation>();
            _filePath = Path.Combine(Interface.Oxide.RootDirectory, $"Oxide.Compiler");
            _pipeName = $"Oxide.Compiler.{Guid.NewGuid()}";

            string downloadUrl = string.Format(Constants.CompilerDownloadUrl, extension.Branch);
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                {
                    _filePath += ".exe";
                    _remoteName = downloadUrl + $"win-x64.AOT.Compiler.exe";
                    break;
                }
                case PlatformID.Unix:
                {
                    _remoteName = downloadUrl + "linux-x64.Compiler";
                    break;
                }
                case PlatformID.MacOSX:
                {
                    throw new PlatformNotSupportedException("macOS is not supported for the compiler");
                }
            }

            EnvironmentHelper.SetVariable("Path:Root", Interface.Oxide.RootDirectory);
            EnvironmentHelper.SetVariable("Path:Logging", Interface.Oxide.LogDirectory);
            EnvironmentHelper.SetVariable("Path:Plugins", Interface.Oxide.PluginDirectory);
            EnvironmentHelper.SetVariable("Path:Configuration", Interface.Oxide.ConfigDirectory);
            EnvironmentHelper.SetVariable("Path:Data", Interface.Oxide.DataDirectory);
            EnvironmentHelper.SetVariable("Path:Libraries", Interface.Oxide.ExtensionDirectory);

            if (Interface.Oxide.Config.Compiler.Publicize ?? false)
            {
                EnvironmentHelper.SetVariable("AllowPublicize", "true", force: true);
            }
        }

        private void ExpireFileCache()
        {
            lock (CompilerFile.FileCache)
            {
                object[] toRemove = ArrayPool<object>.Shared.Take(CompilerFile.FileCache.Count);
                int index = 0;
                foreach (KeyValuePair<string, CompilerFile> file in CompilerFile.FileCache)
                {
                    if (file.Value.KeepCached)
                    {
                        continue;
                    }

                    toRemove[index] = file.Key;
                    index++;
                }

                for (int i = 0; i < index; i++)
                {
                    string key = (string)toRemove[i];
                    Log(LogType.Info, $"Removing cached dependency {Path.GetFileName(key)}");
                    CompilerFile.FileCache.Remove(key);
                }

                ArrayPool<object>.Shared.Return(toRemove);
            }
        }

        internal bool Precheck()
        {
            List<string> preprocessorList = new List<string>
            {
                "OXIDE",
                "OXIDEMOD"
            };

            Extension game = Interface.Oxide.GetAllExtensions().SingleOrDefault(e => e.IsGameExtension);

            if (game != null)
            {
                string name = game.Name.ToUpperInvariant();
                string branch = game.Branch?.ToUpperInvariant() ?? "PUBLIC";
                preprocessorList.Add(EscapeSymbolName(name));
                preprocessorList.Add(EscapeSymbolName(name + "_" + branch));

                if (game.Version != default)
                {
                    preprocessorList.Add(EscapeSymbolName(name + "_" + game.Version));
                    preprocessorList.Add(EscapeSymbolName(name + "_" + game.Version + "_" + branch));
                }
            }

            foreach (Extension extension in Interface.Oxide.GetAllExtensions())
            {
                try
                {
                    string prefix = $"{extension.Name.ToUpper()}_EXT";
                    foreach (string directive in extension.GetPreprocessorDirectives())
                    {
                        if (!extension.IsGameExtension && !extension.IsCoreExtension && !directive.StartsWith(prefix))
                        {
                            Interface.Oxide.LogWarning("Missing extension preprocessor prefix '{0}' for directive '{1}' (by extension '{2}')", prefix, directive, extension.Name);
                        }

                        preprocessorList.Add(EscapeSymbolName(directive));
                    }
                }
                catch (Exception ex)
                {
                    Interface.Oxide.LogException($"An error occurred processing preprocessor directives for extension `{extension.Name}`", ex);
                }
            }

#if DEBUG
            preprocessorList.Add("DEBUG");
#endif

            if (Interface.Oxide.Config.Compiler.PreprocessorDirectives.Count > 0)
            {
                preprocessorList.AddRange(Interface.Oxide.Config.Compiler.PreprocessorDirectives);
            }

            if (Interface.Oxide.Config.Compiler.Publicize ?? false)
            {
                EnvironmentHelper.SetVariable("AllowPublicize", "true", force: true);
                preprocessorList.Add("OXIDE_PUBLICIZED");
            }

            _preprocessor = preprocessorList.Distinct().ToArray();

#if DEBUG
            Log(LogType.Debug, $"Preprocessors are: {string.Join(", ", _preprocessor)}");
#endif


            /*if (!DownloadFile(remoteName, filePath, 3))
            {
                return false;
            }*/

            return SetFilePermissions(_filePath);
        }

        private bool Start()
        {
            if (_filePath == null)
            {
                return false;
            }

            if (_compilerProcess != null && _compilerProcess.Handle != IntPtr.Zero && !_compilerProcess.HasExited)
            {
                return true;
            }

            try
            {
                int attempts = 0;
                while (!File.Exists(_filePath))
                {
                    attempts++;
                    if (attempts > 3)
                    {
                        throw new IOException($"Compiler failed to download after 3 attempts");
                    }

                    Log(LogType.Error, $"Compiler doesn't exist at {_filePath}, attempting to download again | Attempt: {attempts} of 3");
                    Precheck();
                    Thread.Sleep(100);
                }
            }
            catch (Exception e)
            {
                Log(LogType.Error, e.Message);
                return false;
            }

            Stop(false, "starting new process");
            _startTime = Interface.Oxide.Now;
            string args = Constants.CompilerBasicArguments + $" --parent {Process.GetCurrentProcess().Id} --pipe {_pipeName} -l:file \"{Path.Combine(Interface.Oxide.LogDirectory, $"oxide.compiler_{DateTime.Now:yyyy-MM-dd}.log")}\"";
#if DEBUG
            args += " -v debug";
#endif
            Log(LogType.Info, $"Starting compiler with parameters: {args}");
            try
            {
                _compilerProcess = new Process
                {
                    StartInfo =
                    {
                        FileName = _filePath,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        Arguments = args
                    },

                    EnableRaisingEvents = true
                };

                _compilerProcess.Exited += OnCompilerProcessExited;
                _compilerProcess.Start();
            }
            catch (Exception ex)
            {
                _compilerProcess?.Dispose();
                _compilerProcess = null;
                Interface.Oxide.LogException($"Exception while starting compiler", ex);
                if (_filePath.Contains("'"))
                {
                    Interface.Oxide.LogError("Server directory path contains an apostrophe, compiler will not work until path is renamed");
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Interface.Oxide.LogError("Compiler may not be set as executable; chmod +x or 0744/0755 required");
                }

                if (ex.GetBaseException() != ex)
                {
                    Interface.Oxide.LogException("BaseException: ", ex.GetBaseException());
                }

                Win32Exception win32 = ex as Win32Exception;
                if (win32 != null)
                {
                    Interface.Oxide.LogError($"Win32 NativeErrorCode: {win32.NativeErrorCode} ErrorCode: {win32.ErrorCode} HelpLink: {win32.HelpLink}");
                }
            }

            if (_compilerProcess == null)
            {
                return false;
            }

            _messageBrokerService = new MessageBrokerService();
            _messageBrokerService.OnMessageReceived += OnMessageReceived;
            _messageBrokerService.Start(_pipeName);
            ResetIdleTimer();
            Interface.Oxide.LogInfo($"[CSharp] Started Oxide.Compiler v{GetCompilerVersion()} successfully");
            return true;
        }

        private void OnMessageReceived(CompilerMessage message)
        {
            if (message == null)
            {
                //Stop(true, "invalid message sent");
                return;
            }

            Interface.Oxide.LogInfo($"Received message from compiler of type {message.Type}");

            switch (message.Type)
            {
                case MessageType.Data:
                {
                    Compilation compilation = _compilations[message.Id];
                    if (compilation == null)
                    {
                        Log(LogType.Error, "Compiler compiled an unknown assembly"); // TODO: Any way to clarify this?
                        return;
                    }

                    compilation.endedAt = Interface.Oxide.Now;
                    string stdOutput = (string)message.ExtraData;
                    if (stdOutput != null)
                    {
                        foreach (string line in stdOutput.Split(new[] { '\r', '\n' },
                                     StringSplitOptions.RemoveEmptyEntries))
                        {
                            Match match = Constants.FileErrorRegex.Match(line.Trim());
                            if (!match.Success)
                            {
                                continue;
                            }

                            if (match.Groups["Severity"].Value != "Error")
                            {
                                continue;
                            }

                            string fileName = match.Groups["File"].Value;
                            string scriptName = Path.GetFileNameWithoutExtension(fileName);
                            string error = match.Groups["Message"].Value;

                            CompilablePlugin compilablePlugin =
                                compilation.plugins.SingleOrDefault(pl => pl.ScriptName == scriptName);

                            if (compilablePlugin == null)
                            {
                                Interface.Oxide.LogError($"Unable to resolve script error to {fileName}: {error}");
                                continue;
                            }

                            IEnumerable<string> missingRequirements =
                                compilablePlugin.Requires.Where(name => !compilation.IncludesRequiredPlugin(name));

                            string[] missingRequirementsArray = missingRequirements.ToArray();
                            if (missingRequirementsArray.Length > 0)
                            {
                                compilablePlugin.CompilerErrors = $"Missing dependencies: {string.Join(",", missingRequirementsArray)}";

                                Log(LogType.Error, $"[{match.Groups["Severity"].Value}][{scriptName}] Missing dependencies: {string.Join(",", missingRequirementsArray)}");
                            }
                            else
                            {
                                compilablePlugin.CompilerErrors = error.Trim()
                                    .Replace(Interface.Oxide.PluginDirectory + Path.DirectorySeparatorChar,
                                        string.Empty);
                            }
                        }
                    }

                    CompilationResult compilationResult = Constants.Serializer.Deserialize<CompilationResult>(message.Data);
                    if (compilationResult.Data == null || compilationResult.Data.Length == 0)
                    {
                        compilation.Completed();
                    }
                    else
                    {
                        compilation.Completed(compilationResult.Data, compilationResult.Symbols);
                    }

                    _compilations.Remove(message.Id);

                    break;
                }
                case MessageType.Error:
                {
                    Exception exception = (Exception)message.ExtraData;
                    Compilation compilation = _compilations[message.Id];
                    _compilations.Remove(message.Id);

                    if (compilation == null)
                    {
                        Interface.Oxide.LogException("Compiler returned a error for a untracked compilation", exception);
                        return;
                    }

                    foreach (CompilablePlugin p in compilation.plugins)
                    {
                        p.CompilerErrors = exception.Message;
                    }

                    compilation.Completed();
                    break;
                }
                case MessageType.Ready:
                {
                    string logMessage =
                        $"Ready signal received from compiler (Startup took: {Math.Round((Interface.Oxide.Now - _startTime) * 1000f)}ms)";
                    switch (_messageQueue.Count)
                    {
                        case 0:
                        {
                            Log(LogType.Info, logMessage);
                            break;
                        }
                        case 1:
                        {
                            Log(LogType.Info, logMessage + ", sending compilation. . .");
                            break;
                        }
                        default:
                        {
                            Log(LogType.Info, logMessage + $", sending {_messageQueue.Count} compilations. . .");
                            break;
                        }
                    }

                    if (!_ready)
                    {
                        _ready = true;

                        while (_messageQueue.TryDequeue(out CompilerMessage compilerMessage))
                        {
                            _compilations[compilerMessage.Id].startedAt = Interface.Oxide.Now;
                            _messageBrokerService?.SendMessage(compilerMessage);
                        }
                    }

                    break;
                }
            }

            Interface.Oxide.NextTick(ResetIdleTimer);
        }

        internal void Stop(bool synchronous, string reason)
        {
            _ready = false;
            Process? compilerProcess = _compilerProcess;
            if (compilerProcess == null)
            {
                return;
            }

            _compilerProcess = null;
            compilerProcess.Exited -= OnCompilerProcessExited;
            compilerProcess.Refresh();

            if (!string.IsNullOrEmpty(reason))
            {
                Interface.Oxide.LogInfo($"Shutting down compiler because {reason}");
            }

            if (!compilerProcess.HasExited)
            {
                _messageBrokerService.SendShutdownMessage();

                if (synchronous)
                {
                    if (compilerProcess.WaitForExit(10000))
                    {
                        Interface.Oxide.LogInfo("Compiler shutdown completed");
                    }
                    else
                    {
                        Interface.Oxide.LogWarning(
                            "Compiler failed to gracefully shutdown, killing the process...");
                        compilerProcess.Kill();
                    }

                    compilerProcess.Close();
                }
                else
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (compilerProcess.WaitForExit(50000))
                        {
                            Interface.Oxide.LogInfo("Compiler shutdown completed");
                        }
                        else
                        {
                            Interface.Oxide.LogWarning(
                                "Compiler failed to gracefully shutdown, killing the process...");
                            compilerProcess.Kill();
                        }

                        compilerProcess.Close();
                    });
                }
            }
            else
            {
                compilerProcess.Close();
                Log(LogType.Info, "Released compiler resources");
            }

            _messageBrokerService.OnMessageReceived -= OnMessageReceived;
            _messageBrokerService.Stop();
            _messageBrokerService = null;

            ExpireFileCache();
        }

        private void OnError(Exception exception) => OnCompilerFailed($"Compiler threw a error: {exception}");

        private void OnCompilerProcessExited(object sender, EventArgs eventArgs)
        {
            Interface.Oxide.NextTick(() =>
            {
                OnCompilerFailed($"compiler was closed unexpectedly");

                string envPath = Environment.GetEnvironmentVariable("PATH");
                string libraryPath = Path.Combine(Interface.Oxide.ExtensionDirectory, ".dotnet");

                if (string.IsNullOrEmpty(envPath) || !envPath.Contains(libraryPath))
                {
                    Log(LogType.Warning, $"PATH does not contain path to compiler dependencies: {libraryPath}");
                }
                else
                {
                    Log(LogType.Warning, "User running server may not have the proper permissions or install is missing files");
                }

                Stop(false, "process exited");
            });
        }

        private void ResetIdleTimer()
        {
            _idleTimer?.Destroy();

            if (!Interface.Oxide.Config.Compiler.IdleShutdown)
            {
                return;
            }

            _idleTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Once(Interface.Oxide.Config.Compiler.IdleTimeout,
                () => Stop(false, "idle shutdown"));
        }

        internal void Compile(CompilablePlugin[] plugins, Action<Compilation> callback)
        {
            ResetIdleTimer();
            int id = _lastId++;
            Compilation compilation = new Compilation(id, callback, plugins);
            _compilations[id] = compilation;
            compilation.Prepare(() => EnqueueCompilation(compilation));
        }

        internal void OnCompileTimeout() => Stop(false, "compiler timeout");

        private void EnqueueCompilation(Compilation compilation)
        {
            if (compilation.plugins.Count < 1)
            {
                return;
            }

            if ((!Installed && !Precheck()) || !Start())
            {
                OnCompilerFailed($"compiler couldn't be started");
                Stop(false, "failed to start");
                return;
            }

            compilation.Started();

            HashSet<string> includedFiles = new HashSet<string>();

            List<CompilerFile> sourceFiles = new List<CompilerFile>();
            foreach (CompilablePlugin plugin in compilation.plugins)
            {
                string name = Path.GetFileName(plugin.ScriptPath ?? plugin.ScriptName);
                if (plugin.ScriptSource == null || plugin.ScriptSource.Length == 0)
                {
                    plugin.CompilerErrors = "No data contained in .cs file";
                    Log(LogType.Error, $"Ignoring plugin {name}, file is empty");
                    continue;
                }

                foreach (string include in plugin.IncludePaths)
                {
                    if (includedFiles.Contains(include))
                    {
                        Interface.Oxide.LogWarning($"Tried to include {include} but it has already been added to the compilation");
                        continue;
                    }

                    CompilerFile includeFile = new CompilerFile(include);
                    if (includeFile.Data == null || includeFile.Data.Length == 0)
                    {
                        Interface.Oxide.LogWarning($"Ignoring plugin {includeFile.Name}, file is empty");
                        continue;
                    }

                    Interface.Oxide.LogWarning($"Adding {includeFile.Name} to compilation project");

                    sourceFiles.Add(includeFile);
                    includedFiles.Add(include);
                }

                Log(LogType.Info, $"Adding plugin {name} to compilation project");
                sourceFiles.Add(new CompilerFile(plugin.ScriptPath ?? plugin.ScriptName, plugin.ScriptSource));
            }

            if (sourceFiles.Count == 0)
            {
                Interface.Oxide.LogError("Compilation job contained no valid plugins");
                _compilations.Remove(compilation.id);
                compilation.Completed();
                return;
            }

            CompilerData compilerData = new CompilerData
            {
                OutputFile = compilation.name,
                SourceFiles = sourceFiles,
                ReferenceFiles = compilation.references.Values.ToArray(),
                Preprocessor = _preprocessor,
                Debug = Debugger.IsAttached,
            };

            CompilerMessage compilerMessage = new CompilerMessage
            {
                Id = compilation.id,
                Type = MessageType.Data,
                Data = Constants.Serializer.Serialize(compilerData),
            };

            if (_ready)
            {
                compilation.startedAt = Interface.Oxide.Now;
                _messageBrokerService.SendMessage(compilerMessage);
            }
            else
            {
                _messageQueue.Enqueue(compilerMessage);
            }
        }

        private void OnCompilerFailed(string reason)
        {
            foreach (Compilation compilation in _compilations.Values)
            {
                foreach (CompilablePlugin plugin in compilation.plugins)
                {
                    plugin.CompilerErrors = reason;
                }

                compilation.Completed();
            }

            _compilations.Clear();
        }

        private static bool SetFilePermissions(string filePath)
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                {
                    break;
                }
                default:
                {
                    return true;
                }
            }

            string name = Path.GetFileName(filePath);
            try
            {
                if (Syscall.access(filePath, AccessModes.X_OK) == 0)
                {
                    Log(LogType.Info, $"{name} is executable");
                }
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogException($"Unable to check {name} for executable permission", ex);
            }
            try
            {
                Syscall.chmod(filePath, FilePermissions.S_IRWXU);
                Interface.Oxide.LogInfo($"File permissions set for {name}");
                return true;
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogException($"Could not set {filePath} as executable, please set manually", ex);
            }

            return false;
        }

        private static bool DownloadFile(string url, string path, int retries = 3)
        {
            string fileName = Path.GetFileName(path);
            int retry = 0;
            string md5 = null;
            try
            {
                DateTime? last = null;
                if (File.Exists(path))
                {
                    md5 = GenerateFileHash(path);
                    last = File.GetLastWriteTimeUtc(path);
                    string msg = $"[CSharp] Checking for updates for {fileName} | Local MD5: {md5}";

                    if (last.HasValue)
                    {
                        msg += $" | Last modified: {last.Value:yyyy-MM-dd HH:mm:ss}";
                    }

                    Interface.Oxide.LogInfo(msg);
                }
                else
                {
                    Interface.Oxide.LogInfo($"[CSharp] Downloading {fileName}. . .");
                }

                byte[] data;
                int code;
                bool newerFound;
                if (!TryDownload(url, retries, ref retry, last, out data, out code, out newerFound, ref md5))
                {
                    string attemptVerb = retries == 1 ? "attempt" : "attempts";
                    Interface.Oxide.LogError($"[CSharp] Failed to download {fileName} after {retry} {attemptVerb} with response code '{code}', please manually download it from {url} and save it here {path}");
                    return false;
                }

                if (data != null)
                {
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(data, 0, data.Length);
                    }

                    if (newerFound)
                    {
                        string checkVerb = md5 != null ? $"Remote MD5: {md5}" : "Newer found";
                        Interface.Oxide.LogInfo($"[CSharp] Downloaded newer version of {fileName} | {checkVerb}");
                    }
                    else
                    {
                        Interface.Oxide.LogInfo($"[CSharp] Downloaded {fileName}");
                    }
                }
                else
                {
                    Interface.Oxide.LogInfo($"[CSharp] {fileName} is up to date");
                }

                return true;
            }
            catch (Exception e)
            {
                Interface.Oxide.LogException($"Unexpected error occurred while trying to download {fileName}, please manually download it from {url} and save it here {path}", e);
                return false;
            }
        }

        private static bool TryDownload(string url, int retries, ref int current, DateTime? lastModified, out byte[] data, out int code, out bool newerFound, ref string md5)
        {
            newerFound = true;
            data = null;
            code = -1;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.AllowAutoRedirect = true;
                request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);

                if (!string.IsNullOrEmpty(md5))
                {
                    string md5msg = $"Validating checksum with server for {Path.GetFileName(url)} | Local: {md5}";
                    int md5retries = 0;
                    string servermd5 = null;
                    if (TryDownload(url + ".md5", retries, ref md5retries, null, out byte[] md5data, out int md5code, out bool _, ref servermd5) && md5code == 200)
                    {

                        servermd5 = Encoding.UTF8.GetString(md5data).Trim();

                        if (string.IsNullOrEmpty(servermd5))
                        {
                            servermd5 = "N/A";
                        }

                        md5msg += $" | Server: {servermd5}";

                        if (servermd5.Equals(md5, StringComparison.InvariantCultureIgnoreCase))
                        {
                            md5 = servermd5;
                            newerFound = false;
                            md5msg += " | Match!";
                            Log(LogType.Debug, md5msg);
                            return true;
                        }

                        md5 = servermd5;
                        md5msg += " | No Match!";
                        Log(LogType.Warning, md5msg);
                    }
                    else if (lastModified.HasValue)
                    {
                        md5 = null;
                        Log(LogType.Warning, $"Failed to download {url}.md5 after {md5retries} attempts with response code '{md5code}', using last modified date instead");
                        request.IfModifiedSince = lastModified.Value;
                    }
                }
                else if (lastModified.HasValue)
                {
                    request.IfModifiedSince = lastModified.Value;
                }

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                int statusCode = (int)response.StatusCode;
                code = statusCode;
                switch (statusCode)
                {
                    case 304:
                    {
                        newerFound = false;
                        return true;
                    }
                    case 200:
                    {
                        break;
                    }
                    default:
                    {
                        if (current <= retries)
                        {
                            current++;
                            Thread.Sleep(1000);
                            return TryDownload(url, retries, ref current, lastModified, out data, out code,
                                out newerFound, ref md5);
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                MemoryStream fs = new MemoryStream();
                Stream stream = response.GetResponseStream();
                int bufferSize = 10000;
                byte[] buffer = new byte[bufferSize];
                while (true)
                {
                    int result = stream.Read(buffer, 0, bufferSize);
                    if (result == -1 || result == 0)
                    {
                        break;
                    }

                    fs.Write(buffer, 0, result);
                }
                data = fs.ToArray();
                fs.Close();
                stream.Close();
                response.Close();

                return true;
            }
            catch (WebException webex)
            {
                if (webex.Response != null)
                {
                    HttpWebResponse r = (HttpWebResponse)webex.Response;
                    code = (int)r.StatusCode;
                    switch (r.StatusCode)
                    {
                        case HttpStatusCode.NotModified:
                            newerFound = false;
                            return true;
                        default:
                            if (current <= retries)
                            {
                                current++;
                                Thread.Sleep(1000);
                                return TryDownload(url, retries, ref current, lastModified, out data, out code, out newerFound, ref md5);
                            }
                            else
                            {
                                return false;
                            }
                    }
                }
            }
            return false;
        }

        private static void Log(LogType type, string message, Exception exception = null) => Interface.Oxide.RootLogger.WriteDebug(type, LogEvent.Compile, "CSharp", message, exception);

        private string GetCompilerVersion()
        {
            if (!Installed)
            {
                return "0.0.0";
            }

            FileVersionInfo version = FileVersionInfo.GetVersionInfo(_filePath);
            return version.FileVersion;
        }

        private static string GenerateFileHash(string file)
        {
            using (MD5 md5 = MD5.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>
        /// This allows to handle cases where injected symbols have inappropriate characters (e.g. git branches can have "/", "-" etc. while #define disallows them)
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string EscapeSymbolName(string name)
        {
            return Constants.SymbolEscapeRegex.Replace(name, "_");
        }
    }
}
