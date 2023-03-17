extern alias References;

using ObjectStream;
using ObjectStream.Data;
using Oxide.Core;
using Oxide.Core.Logging;
using Oxide.Logging;
using Oxide.Plugins;
using References::Mono.Unix.Native;
using System;
using System.CodeDom.Compiler;
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
        private Hash<int, Compilation> compilations;
        private Queue<CompilerMessage> messageQueue;
        private Process process;
        private volatile int lastId;
        private volatile bool ready;
        private Core.Libraries.Timer.TimerInstance idleTimer;
        private ObjectStreamClient<CompilerMessage> client;
        private string filePath;
        private string remoteName;
        private string dotnet;
        private string dotnetInstall;
        private string dotnetInstallScript;
        private static PlatformID PlatformID;
        private static Regex fileErrorRegex = new Regex(@"^\[(?'Severity'\S+)\]\[(?'Code'\S+)\]\[(?'File'\S+)\] (?'Message'.+)$", RegexOptions.Compiled);
        public bool Installed => File.Exists(filePath);
        public CompilerService()
        {
            compilations = new Hash<int, Compilation>();
            messageQueue = new Queue<CompilerMessage>();
            string arc = IntPtr.Size == 8 ? "x64" : "x86";
            filePath = Path.Combine(Interface.Oxide.RootDirectory, $"Compiler");
            remoteName = $"Compiler.min.{arc}";
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                    PlatformID = PlatformID.Win32Windows;
                    filePath += ".exe";
                    remoteName += "-win.exe";
                    dotnet = "dotnet.exe";
                    dotnetInstall = "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1";
                    dotnetInstallScript = "dotnet-install.ps1";
                    break;

                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    PlatformID = PlatformID.Unix;
                    remoteName += "-unix";
                    dotnet = "dotnet";
                    dotnetInstall = "https://download.visualstudio.microsoft.com/download/pr/2431d5ac-f5db-4bb1-bcf0-4a2d9725d4e4/1b0747add72af919754509f83ad08660/dotnet-runtime-7.0.3-linux-x64.tar.gz";
                    dotnetInstallScript = "dotnet.tar.gz";
                    break;
            }
        }

        internal bool Precheck()
        {
            if (HasDotNetInstalled(dotnet, dotnetInstall, dotnetInstallScript))
            {
                Log(LogType.Info, ".NET 7 is installed, minified version of Oxide.Compiler selected.");
            }
            else
            {
                remoteName = remoteName.Replace(".min", string.Empty);
                Log(LogType.Info, ".NET 7 not found, packed version of Oxide.Compiler selected.");
            }

            if (!DownloadFile($"http://cdn.oxidemod.cloud/compiler/{remoteName}", filePath, 3))
            {
                return false;
            }

            return SetFilePermissions(filePath);
        }

        private bool Start()
        {
            if (filePath == null)
            {
                return false;
            }

            if (process != null && process.Handle != IntPtr.Zero && !process.HasExited)
            {
                return true;
            }

            Stop();

            Dictionary<string, string> settings = new Dictionary<string, string>()
            {
                ["Path:Root"] = Interface.Oxide.RootDirectory,
                ["Path:Logging"] = Interface.Oxide.LogDirectory,
                ["Path:Plugins"] = Interface.Oxide.PluginDirectory,
                ["Path:Configuration"] = Interface.Oxide.ConfigDirectory,
                ["Path:Data"] = Interface.Oxide.DataDirectory,
                ["Path:Libraries"] = Interface.Oxide.ExtensionDirectory,
                ["Compiler:AllowUnsafe"] = "true",
                ["Compiler:UseStandardLibraries"] = "false",
                ["Compiler:Force"] = "true",
                ["Logging:FileName"] = Path.Combine(Interface.Oxide.LogDirectory, "compiler.log"),
#if DEBUG
                ["Logging:Level"] = "Debug"
#else
                ["Logging:Level"] = "Information"
#endif
            };

            foreach (var setting in settings)
            {
                EnvironmentHelper.SetOxideEnvironmentalVariable(setting.Key, setting.Value);
            }

            EnvironmentHelper.SetOxideEnvironmentalVariable("Compiler:EnableMessageStream", "true", true);

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
                        RedirectStandardOutput = true
                    },
                    EnableRaisingEvents = true
                };
                process.Exited += OnProcessExited;
                process.Start();
                Log(LogType.Info, "Compiler has been started successfully");
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
            Interface.Oxide.NextTick(() =>
            {
                idleTimer?.Destroy();
                idleTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Once(60, Stop);
            });
            return true;
        }

        internal void Stop()
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

            if (!endedProcess.HasExited)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    stream.PushMessage(new CompilerMessage { Type = CompilerMessageType.Exit });
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
                Stop();
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
                                continue;

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
                    Interface.Oxide.NextTick(() =>
                    {
                        idleTimer?.Destroy();
                        idleTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Once(60, Stop);
                    });
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

                    Interface.Oxide.NextTick(() =>
                    {
                        idleTimer?.Destroy();
                        idleTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Once(60, Stop);
                    });
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

                Stop();
            });
        }

        internal void Compile(CompilablePlugin[] plugins, Action<Compilation> callback)
        {
            int id = lastId++;
            Compilation compilation = new Compilation(id, callback, plugins);
            compilations[id] = compilation;
            compilation.Prepare(() => EnqueueCompilation(compilation));
        }

        internal void OnCompileTimeout() => Stop();

        private void EnqueueCompilation(Compilation compilation)
        {
            if (compilation.plugins.Count < 1)
            {
                return;
            }

            if ((!Installed && !Precheck()) || !Start())
            {
                OnCompilerFailed($"compiler couldn't be started");
                Stop();
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

        private static bool HasDotNetInstalled(string dotnet, string url, string script)
        {
            string localDir = Path.Combine(Interface.Oxide.ExtensionDirectory, ".dotnet");
            try
            {
                bool isInstalled = ScanPath(dotnet, out string fullPath);

                if (!isInstalled)
                {
                    fullPath = Path.Combine(localDir, dotnet);

                    if (File.Exists(fullPath))
                    {
                        Environment.SetEnvironmentVariable("DOTNET_ROOT", localDir);
                        AppendPathVariable(localDir);
                        AppendPathVariable(Path.Combine(localDir, "tools"));
                        Log(LogType.Info, "Local installation of dotnet found");
                        isInstalled = true;
                    }
                    else
                    {
                        string installScript = Path.Combine(Interface.Oxide.RootDirectory, script);

                        if (DownloadFile(url, installScript, 2) && SetFilePermissions(installScript))
                        {
                            string prog;
                            string args;
                            if (PlatformID == PlatformID.Unix)
                            {
                                prog = "tar";
                                args = $"-xzf '{installScript}' -C '{localDir + Path.DirectorySeparatorChar}'";
                                if (!Directory.Exists(localDir))
                                {
                                    Directory.CreateDirectory(localDir);
                                }
                            }
                            else
                            {
                                prog = "powershell.exe";
                                args = $"& '{installScript}' -Channel 7.0 -InstallDir \"{localDir}\" -Runtime dotnet -NoPath";
                            }

                            Process process = Process.Start(new ProcessStartInfo(prog, args));
                            process.WaitForExit();
                            Cleanup.Add(installScript);

                            Environment.SetEnvironmentVariable("DOTNET_ROOT", localDir);
                            AppendPathVariable(localDir);
                            AppendPathVariable(Path.Combine(localDir, "tools"));
                            Log(LogType.Info, "Local installation of dotnet downloaded");
                            isInstalled = true;
                        }
                    }
                }
                else
                {
                    Log(LogType.Info, "Global installation of .NET 7 found");

                    if (Directory.Exists(localDir))
                    {
                        Directory.Delete(localDir, true);
                        Log(LogType.Warning, "Deleting local install of .NET 7 to reclaim disk space");
                    }
                }

                if (!isInstalled)
                {
                    Log(LogType.Error, "Failed to locate or install dotnet please manually install .NET 7 from https://dotnet.microsoft.com/en-us/download/dotnet/7.0");
                    return false;
                }

                Process dot = Process.Start(new ProcessStartInfo(fullPath, "--list-runtimes")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });

                dot.WaitForExit();

                string sdksFull = dot.StandardOutput.ReadToEnd();
                string[] sdks = sdksFull.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return sdks != null && sdks.Any(s => s.StartsWith("Microsoft.NETCore.App 7."));
            }
            catch (Exception e)
            {
                Log(LogType.Error, "Failed to locate or install dotnet please manually install .NET 7 from https://dotnet.microsoft.com/en-us/download/dotnet/7.0", exception: e);
                return false;
            }
        }

        private static bool ScanPath(string file, out string fullPath)
        {
            fullPath = null;
            string[] paths = Environment.GetEnvironmentVariable("PATH").Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string path in paths)
            {
                string filePath = Path.Combine(path, file);

                if (File.Exists(filePath))
                {
                    fullPath = filePath;
                    return true;
                }
            }

            return false;
        }

        private static void AppendPathVariable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string PATH = Environment.GetEnvironmentVariable("PATH");
            PATH += Path.PathSeparator + path;
            Environment.SetEnvironmentVariable("PATH", PATH);
        }

        private static void Log(LogType type, string message, Exception exception = null) => Interface.Oxide.RootLogger.WriteDebug(type, LogEvent.Compile, "CSharp", message, exception);
    }
}
