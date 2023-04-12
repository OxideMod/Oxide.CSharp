extern alias References;

using ObjectStream;
using ObjectStream.Data;
using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.Logging;
using Oxide.Plugins;
using References::Mono.Unix.Native;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Oxide.CSharp
{
    internal class CompilerService
    {
        private const string baseUrl = "http://cdn.oxidemod.cloud/compiler/";
        private Hash<int, Compilation> compilations;
        private Queue<CompilerMessage> messageQueue;
        private Process process;
        private volatile int lastId;
        private volatile bool ready;
        private Core.Libraries.Timer.TimerInstance idleTimer;
        private Core.Libraries.Timer.TimerInstance expireTimer;
        private ObjectStreamClient<CompilerMessage> client;
        private string filePath;
        private string remoteName;
        private string compilerBasicArguments = "-unsafe true --setting:Force true -ms true";
        private static Regex fileErrorRegex = new Regex(@"^\[(?'Severity'\S+)\]\[(?'Code'\S+)\]\[(?'File'\S+)\] (?'Message'.+)$", RegexOptions.Compiled);
        public bool Installed => File.Exists(filePath);

        public CompilerService()
        {
            compilations = new Hash<int, Compilation>();
            messageQueue = new Queue<CompilerMessage>();
            string arc = IntPtr.Size == 8 ? "x64" : "x86";
            filePath = Path.Combine(Interface.Oxide.RootDirectory, $"Compiler");
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                    filePath += ".exe";
                    remoteName = baseUrl + $"win-{arc}.Compiler.exe";
                    break;

                case PlatformID.MacOSX:
                    remoteName = baseUrl + "osx-x64.Compiler";
                    break;

                case PlatformID.Unix:
                    remoteName = baseUrl + "linux-x64.Compiler";
                    break;
            }

            EnvironmentHelper.SetOxideEnvironmentalVariable("Path:Root", Interface.Oxide.RootDirectory);
            EnvironmentHelper.SetOxideEnvironmentalVariable("Path:Logging", Interface.Oxide.LogDirectory);
            EnvironmentHelper.SetOxideEnvironmentalVariable("Path:Plugins", Interface.Oxide.PluginDirectory);
            EnvironmentHelper.SetOxideEnvironmentalVariable("Path:Configuration", Interface.Oxide.ConfigDirectory);
            EnvironmentHelper.SetOxideEnvironmentalVariable("Path:Data", Interface.Oxide.DataDirectory);
            EnvironmentHelper.SetOxideEnvironmentalVariable("Path:Libraries", Interface.Oxide.ExtensionDirectory);
        }

        private void ExpireFileCache()
        {
            lock (CompilerFile.FileCache)
            {
                object[] toRemove = ArrayPool.Get(CompilerFile.FileCache.Count);
                int index = 0;
                DateTime now = DateTime.Now;
                foreach (var file in CompilerFile.FileCache)
                {
                    if (file.Value.KeepCached)
                    {
                        continue;
                    }

                    if (now - file.Value.LastRead > TimeSpan.FromMinutes(3))
                    {
                        toRemove[index] = file.Key;
                        index++;
                    }
                }

                for (int i = 0; i < index; i++)
                {
                    string key = (string)toRemove[i];
                    Log(LogType.Info, $"Removing cached dependency {Path.GetFileName(key)}");
                    CompilerFile.FileCache.Remove(key);
                }

                ArrayPool.Free(toRemove);

                if (CompilerFile.FileCache.Count == 0)
                {
                    expireTimer?.Destroy();
                    expireTimer = null;
                }

                if (index > 0)
                {
                    GC.Collect();
                }
            }
        }

        internal bool Precheck()
        {
            if (!DownloadFile(remoteName, filePath, 3))
            {
                return false;
            }

            return SetFilePermissions(filePath);
        }

        private bool Start()
        {
            expireTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Repeat(35, -1, ExpireFileCache);
            if (filePath == null)
            {
                return false;
            }

            if (process != null && process.Handle != IntPtr.Zero && !process.HasExited)
            {
                return true;
            }

            Stop(false, "starting new process");

            string args = compilerBasicArguments + $" --parent {Process.GetCurrentProcess().Id} -l:file compiler_{DateTime.Now.ToString("yyyy-MM-dd")}.log";
#if DEBUG
            args += " -v Debug";
#endif
            Log(LogType.Info, $"Starting compiler with parameters: {args}");
            try
            {
                process = new Process
                {
                    StartInfo =
                    {
                        FileName = filePath,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        Arguments = args
                    },
                    EnableRaisingEvents = true
                };
                process.Exited += OnProcessExited;
                process.Start();
            }
            catch (Exception ex)
            {
                process?.Dispose();
                process = null;
                Log(LogType.Error, $"Exception while starting compiler", exception: ex);
                if (filePath.Contains("'"))
                {
                    Log(LogType.Error, "Server directory path contains an apostrophe, compiler will not work until path is renamed");
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Log(LogType.Error, "Compiler may not be set as executable; chmod +x or 0744/0755 required");
                }

                if (ex.GetBaseException() != ex)
                {
                    Log(LogType.Error, "BaseException: ", exception: ex.GetBaseException());
                }

                Win32Exception win32 = ex as Win32Exception;
                if (win32 != null)
                {
                    Log(LogType.Error, $"Win32 NativeErrorCode: {win32.NativeErrorCode} ErrorCode: {win32.ErrorCode} HelpLink: {win32.HelpLink}");
                }
            }

            if (process == null)
            {
                return false;
            }

            client = new ObjectStreamClient<CompilerMessage>(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
            client.Message += OnMessage;
            client.Error += OnError;
            client.Start();
            ResetIdleTimer();
            Log(LogType.Info, "Compiler has been started successfully");
            return true;
        }

        internal void Stop(bool synchronous, string reason)
        {
            ready = false;
            Process endedProcess = process;
            ObjectStreamClient<CompilerMessage> stream = client;
            if (endedProcess == null || stream == null)
            {
                return;
            }

            process = null;
            client = null;
            endedProcess.Exited -= OnProcessExited;
            endedProcess.Refresh();
            stream.Message -= OnMessage;
            stream.Error -= OnError;

            if (!string.IsNullOrEmpty(reason))
            {
                Log(LogType.Warning, $"Shutting down compiler because {reason}");
            }

            if (!endedProcess.HasExited)
            {
                stream.PushMessage(new CompilerMessage { Type = CompilerMessageType.Exit });
                if (synchronous)
                {
                    if (endedProcess.WaitForExit(10000))
                    {
                        Log(LogType.Info, "Compiler shutdown completed");
                    }
                    else
                    {
                        Log(LogType.Warning, "Compiler failed to gracefully shutdown, killing the process...");
                        endedProcess.Kill();
                    }

                    stream.Stop();
                    stream = null;
                    endedProcess.Close();
                }
                else
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (endedProcess.WaitForExit(10000))
                        {
                            Log(LogType.Info, "Compiler shutdown completed");
                        }
                        else
                        {
                            Log(LogType.Warning, "Compiler failed to gracefully shutdown, killing the process...");
                            endedProcess.Kill();
                        }

                        stream.Stop();
                        stream = null;
                        endedProcess.Close();
                    });
                }
            }
            else
            {
                stream.Stop();
                stream = null;
                endedProcess.Close();
                Log(LogType.Info, "Released compiler resources");
            }
        }

        private void OnMessage(ObjectStreamConnection<CompilerMessage, CompilerMessage> connection, CompilerMessage message)
        {
            if (message == null)
            {
                //Stop(true, "invalid message sent");
                return;
            }

            switch (message.Type)
            {
                case CompilerMessageType.Assembly:
                    Compilation compilation = compilations[message.Id];
                    if (compilation == null)
                    {
                        Log(LogType.Error, "Compiler compiled an unknown assembly"); // TODO: Any way to clarify this?
                        return;
                    }
                    compilation.endedAt = Interface.Oxide.Now;
                    string stdOutput = (string)message.ExtraData;
                    if (stdOutput != null)
                    {
                        foreach (string line in stdOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            Match match = fileErrorRegex.Match(line.Trim());
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

                            CompilablePlugin compilablePlugin = compilation.plugins.SingleOrDefault(pl => pl.ScriptName == scriptName);

                            if (compilablePlugin == null)
                            {
                                Log(LogType.Error, $"Unable to resolve script error to {fileName}: {error}");
                                continue;
                            }

                            IEnumerable<string> missingRequirements = compilablePlugin.Requires.Where(name => !compilation.IncludesRequiredPlugin(name));

                            if (missingRequirements.Any())
                            {
                                compilablePlugin.CompilerErrors = $"Missing dependencies: {string.Join("," , missingRequirements.ToArray())}";
                                Log(LogType.Error, $"[{match.Groups["Severity"].Value}][{scriptName}] Missing dependencies: {string.Join(",", missingRequirements.ToArray())}");
                            }
                            else
                            {
                                compilablePlugin.CompilerErrors = error.Trim().Replace(Interface.Oxide.PluginDirectory + Path.DirectorySeparatorChar, string.Empty);
                            }
                        }
                    }
                    CompilationResult result = (CompilationResult)message.Data;
                    if (result.Data == null || result.Data.Length == 0)
                    {
                        compilation.Completed();
                    }
                    else
                    {
                        compilation.Completed(result.Data, result.Symbols);
                    }
                    compilations.Remove(message.Id);
                    break;

                case CompilerMessageType.Error:
                    Exception e = (Exception)message.Data;
                    Compilation comp = compilations[message.Id];
                    compilations.Remove(message.Id);

                    if (comp == null)
                    {
                        Log(LogType.Error, "Compiler returned a error for a untracked compilation", e);
                        return;
                    }

                    foreach (var p in comp.plugins)
                    {
                        p.CompilerErrors = e.Message;
                    }

                    comp.Completed();
                    break;

                case CompilerMessageType.Ready:
                    Log(LogType.Info, "Compiler sent the ready signal, starting compilations. . .");
                    connection.PushMessage(message);
                    if (!ready)
                    {
                        ready = true;
                        while (messageQueue.Count > 0)
                        {
                            connection.PushMessage(messageQueue.Dequeue());
                        }
                    }
                    break;
            }

            Interface.Oxide.NextTick(() =>
            {
                ResetIdleTimer();
            });
        }

        private void OnError(Exception exception) => OnCompilerFailed($"Compiler threw a error: {exception.GetType().Name} - {exception.Message}");

        private void OnProcessExited(object sender, EventArgs eventArgs)
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
            if (idleTimer != null)
            {
                idleTimer.Destroy();
            }

            idleTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Once(120f, () => Stop(false, "idle shutdown"));
        }

        internal void Compile(CompilablePlugin[] plugins, Action<Compilation> callback)
        {
            int id = lastId++;
            Compilation compilation = new Compilation(id, callback, plugins);
            compilations[id] = compilation;
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

                foreach (var include in plugin.IncludePaths.Distinct())
                {
                    CompilerFile inc = new CompilerFile(include);
                    if (inc.Data == null || inc.Data.Length == 0)
                    {
                        Log(LogType.Warning, $"Ignoring plugin {inc.Name}, file is empty");
                        continue;
                    }
                    Log(LogType.Info, $"Adding {inc.Name} to compilation project");
                    sourceFiles.Add(inc);
                }

                Log(LogType.Info, $"Adding plugin {name} to compilation project");
                sourceFiles.Add(new CompilerFile(plugin.ScriptPath ?? plugin.ScriptName, plugin.ScriptSource));
            }

            if (sourceFiles.Count == 0)
            {
                Log(LogType.Error, $"Compilation job contained no valid plugins");
                compilations.Remove(compilation.id);
                compilation.Completed();
                return;
            }

            CompilerData data = new CompilerData
            {
                OutputFile = compilation.name,
                SourceFiles = sourceFiles.ToArray(),
                ReferenceFiles = compilation.references.Values.ToArray()
            };

            CompilerMessage message = new CompilerMessage { Id = compilation.id, Data = data, Type = CompilerMessageType.Compile };
            if (ready)
            {
                client.PushMessage(message);
            }
            else
            {
                messageQueue.Enqueue(message);
            }
        }

        private void OnCompilerFailed(string reason)
        {
            foreach (Compilation compilation in compilations.Values)
            {
                foreach (CompilablePlugin plugin in compilation.plugins)
                {
                    plugin.CompilerErrors = reason;
                }

                compilation.Completed();
            }
            compilations.Clear();
        }

        private static bool SetFilePermissions(string filePath)
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    break;

                default:
                    return true;
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
                Log(LogType.Error, $"Unable to check {name} for executable permission", exception: ex);
            }
            try
            {
                Syscall.chmod(filePath, FilePermissions.S_IRWXU);
                Log(LogType.Info, $"File permissions set for {name}");
                return true;
            }
            catch (Exception ex)
            {
                Log(LogType.Error, $"Could not set {filePath} as executable, please set manually", exception: ex);
            }
            return false;
        }

        private static bool DownloadFile(string url, string path, int retries = 3)
        {
            string fileName = Path.GetFileName(path);
            int retry = 0;
            try
            {
                DateTime? last = null;
                if (File.Exists(path))
                {
                    last = File.GetLastWriteTimeUtc(path);
                    Log(LogType.Info, $"{fileName} already exists, checking for updates. . .");
                }
                else
                {
                    Log(LogType.Info, $"Downloading {fileName}. . .");
                }

                byte[] data;
                int code;
                bool newerFound;
                if (!TryDownload(url, retries, ref retry, last, out data, out code, out newerFound))
                {
                    string attemptVerb = retries == 1 ? "attempt" : "attempts";
                    Log(LogType.Error, $"Failed to download {fileName} after {retry} {attemptVerb} with response code '{code}', please manually download it from {url} and save it here {path}");
                    return false;
                }

                if (!newerFound)
                {
                    Log(LogType.Info, $"Latest version of {fileName} already exists");
                }

                if (data != null)
                {
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(data, 0, data.Length);
                    }
                    Log(LogType.Info, $"Latest version of {fileName} has been downloaded");
                }

                return true;
            }
            catch (Exception e)
            {
                Log(LogType.Error, $"Unexpected error occurred while trying to download {fileName}, please manually download it from {url} and save it here {path}", exception: e);
                return false;
            }
        }

        private static bool TryDownload(string url, int retries, ref int current, DateTime? lastModified, out byte[] data, out int code, out bool newerFound)
        {
            newerFound = true;
            data = null;
            code = -1;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.AllowAutoRedirect = true;

                if (lastModified.HasValue)
                {
                    request.IfModifiedSince = lastModified.Value;
                }

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                int statusCode = (int)response.StatusCode;
                code = statusCode;
                switch (statusCode)
                {
                    case 304:
                        newerFound = false;
                        return true;

                    case 200:
                        break;

                    default:
                        if (current <= retries)
                        {
                            current++;
                            Thread.Sleep(1000);
                            return TryDownload(url, retries, ref current, lastModified, out data, out code, out newerFound);
                        }
                        else
                        {
                            return false;
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
                                return TryDownload(url, retries, ref current, lastModified, out data, out code, out newerFound);
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
    }
}
