extern alias References;

using ObjectStream;
using ObjectStream.Data;
using Oxide.Core;
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
                Interface.Oxide.LogInfo("[Compiler] Downloading minified version of the compiler");
            }
            else
            {
                remoteName = remoteName.Replace(".min", string.Empty);
                Interface.Oxide.LogInfo("[Compiler] Downloading packed version of the compiler");
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
            PurgeOldLogs();
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
                ["Compiler:EnableMessageStream"] = "true",
                ["Logging:FileName"] = Path.Combine(Interface.Oxide.LogDirectory, "compiler.log"),
#if DEBUG
                ["Logging:Level"] = "Debug"
#else
                ["Logging:Level"] = "Information"
#endif
            };

            foreach (var setting in settings)
            {
                Environment.SetEnvironmentVariable("OXIDE:" + setting.Key, setting.Value);
            }

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
                Interface.Oxide.LogInfo("Compiler started");
            }
            catch (Exception ex)
            {
                process?.Dispose();
                process = null;
                Interface.Oxide.LogException($"Exception while starting compiler: ", ex);
                if (filePath.Contains("'"))
                {
                    Interface.Oxide.LogWarning("Server directory path contains an apostrophe, compiler will not work until path is renamed");
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Interface.Oxide.LogWarning("Compiler may not be set as executable; chmod +x or 0744/0755 required");
                }

                if (ex.GetBaseException() != ex)
                {
                    Interface.Oxide.LogException("BaseException: ", ex.GetBaseException());
                }

                Win32Exception win32 = ex as Win32Exception;
                if (win32 != null)
                {
                    Interface.Oxide.LogError("Win32 NativeErrorCode: {0} ErrorCode: {1} HelpLink: {2}", win32.NativeErrorCode, win32.ErrorCode, win32.HelpLink);
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
            if (endedProcess != null)
            {
                endedProcess.Exited -= OnProcessExited;
            }

            process = null;
            if (client == null)
            {
                return;
            }

            client.Message -= OnMessage;
            client.Error -= OnError;
            client.PushMessage(new CompilerMessage { Type = CompilerMessageType.Exit });
            client.Stop();
            client = null;

            if (endedProcess == null)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(5000);
                // Calling Close can block up to 60 seconds on certain machines
                if (!endedProcess.HasExited)
                {
                    endedProcess.Kill();
                }
            });
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
                        Interface.Oxide.LogWarning("Compiler compiled an unknown assembly"); // TODO: Any way to clarify this?
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
                                Interface.Oxide.LogError($"Unable to resolve script error to {fileName}: {error}");
                                continue;
                            }

                            IEnumerable<string> missingRequirements = compilablePlugin.Requires.Where(name => !compilation.IncludesRequiredPlugin(name));

                            if (missingRequirements.Any())
                            {
                                compilablePlugin.CompilerErrors = $"Missing dependencies: {string.Join("," , missingRequirements.ToArray())}";
                                Interface.Oxide.LogDebug($"[{match.Groups["Severity"].Value}][{scriptName}] Missing dependencies: {string.Join(",", missingRequirements.ToArray())}");
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
                    Interface.Oxide.LogError("Compilation error: {0}", message.Data);
                    compilations[message.Id].Completed();
                    compilations.Remove(message.Id);
                    Interface.Oxide.NextTick(() =>
                    {
                        idleTimer?.Destroy();
                        idleTimer = Interface.Oxide.GetLibrary<Core.Libraries.Timer>().Once(60, Stop);
                    });
                    break;

                case CompilerMessageType.Ready:
                    Interface.Oxide.LogDebug("[Compiler] Ready Message");
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

        private static void OnError(Exception exception) => Interface.Oxide.LogException("Compilation error: ", exception);

        private void OnProcessExited(object sender, EventArgs eventArgs)
        {
            Interface.Oxide.NextTick(() =>
            {
                OnCompilerFailed($"compiler was closed unexpectedly");

                string envPath = Environment.GetEnvironmentVariable("PATH");
                string libraryPath = Path.Combine(Interface.Oxide.ExtensionDirectory, ".dotnet");

                if (string.IsNullOrEmpty(envPath) || !envPath.Contains(libraryPath))
                {
                    Interface.Oxide.LogWarning($"PATH does not contain path to compiler dependencies: {libraryPath}");
                }
                else
                {
                    Interface.Oxide.LogWarning("User running server may not have the proper permissions or install is missing files");
                }

                Stop();
            });
        }

        private static void PurgeOldLogs()
        {
            try
            {
                IEnumerable<string> filePaths = Directory.GetFiles(Interface.Oxide.LogDirectory, "*.log").Where(f =>
                {
                    string fileName = Path.GetFileName(f);
                    return fileName != null && fileName.StartsWith("compiler_");
                });
                foreach (string filePath in filePaths)
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception)
            {
                // Ignored
            }
        }

        internal void Compile(CompilablePlugin[] plugins, Action<Compilation> callback)
        {
            int id = lastId++;
            Compilation compilation = new Compilation(id, callback, plugins);
            compilations[id] = compilation;
            compilation.Prepare(() => EnqueueCompilation(compilation));
        }


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
            Interface.Oxide.LogDebug("Compiling with references: {0}", string.Join(", ", compilation.references.Keys.ToArray()));
            List<CompilerFile> sourceFiles = compilation.plugins.SelectMany(plugin => plugin.IncludePaths).Distinct().Select(path => new CompilerFile(path)).ToList();
            sourceFiles.AddRange(compilation.plugins.Select(plugin => new CompilerFile(plugin.ScriptPath ?? plugin.ScriptName, plugin.ScriptSource)));
            Interface.Oxide.LogDebug("Compiling files: {0}", string.Join(", ", sourceFiles.Select(f => f.Name).ToArray()));
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
                    Interface.Oxide.LogInfo($"{name} is executable");
                }
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogError($"Unable to check {name} for executable permission");
                Interface.Oxide.LogError(ex.Message);
                Interface.Oxide.LogError(ex.StackTrace);
            }
            try
            {
                Syscall.chmod(filePath, FilePermissions.S_IRWXU);
                Interface.Oxide.LogInfo($"File permissions set for {name}");
                return true;
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogError($"Could not set {filePath} as executable, please set manually");
                Interface.Oxide.LogError(ex.Message);
                Interface.Oxide.LogError(ex.StackTrace);
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
                    Interface.Oxide.LogInfo($"Checking for new version of {fileName}");
                }
                else
                {
                    Interface.Oxide.LogInfo($"Downloading {fileName}. . .");
                }

                byte[] data;
                int code;
                if (!TryDownload(url, retries, ref retry, last, out data, out code))
                {
                    Interface.Oxide.LogError($"Failed to download {fileName} after {retry} attempts with response code '{code}', please manually download it from {url} and save it here {path}");
                    return false;
                }

                if (data != null)
                {
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(data, 0, data.Length);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Interface.Oxide.LogError($"Unexpected error occurred while trying to download {fileName}, please manually download it from {url} and save it here {path}");
                Interface.Oxide.LogError(e.Message);
                Interface.Oxide.LogError(e.StackTrace);
                return false;
            }
        }

        private static bool TryDownload(string url, int retries, ref int current, DateTime? lastModified, out byte[] data, out int code)
        {
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
                        return true;

                    case 200:
                        break;

                    default:
                        if (current <= retries)
                        {
                            current++;
                            Thread.Sleep(1000);
                            return TryDownload(url, retries, ref current, lastModified, out data, out code);
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
                            return true;
                        default:
                            if (current <= retries)
                            {
                                current++;
                                Thread.Sleep(1000);
                                return TryDownload(url, retries, ref current, lastModified, out data, out code);
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
                        Interface.Oxide.LogDebug("[Compiler] Local installation of dotnet found");
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
                            Interface.Oxide.LogDebug("[Compiler] Local installation of dotnet downloaded");
                            isInstalled = true;
                        }
                    }
                }
                else
                {
                    Interface.Oxide.LogDebug("[Compiler] Global installation of dotnet found");

                    if (Directory.Exists(localDir))
                    {
                        Interface.Oxide.LogDebug("[Compiler] Deleting local instance of dotnet to reclaim disk space");
                        Directory.Delete(localDir, true);
                    }
                }

                if (!isInstalled)
                {
                    Interface.Oxide.LogError("[Compiler] Failed to locate or install dotnet please manually install .NET 7 from https://dotnet.microsoft.com/en-us/download/dotnet/7.0");
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
                Interface.Oxide.LogException("[Compiler] Failed to locate or install dotnet please manually install .NET 7 from https://dotnet.microsoft.com/en-us/download/dotnet/7.0", e);
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
    }
}
