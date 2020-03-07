// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : OutOfProcessNodeInstance.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core.NodeServices.HostingModels {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Abstractions;
    using Util;

    /// <summary>
    /// Class responsible for launching a Node child process on the local machine, determining when it is ready to
    /// accept invocations, detecting if it dies on its own, and finally terminating it on disposal.
    ///
    /// This abstract base class uses the input/output streams of the child process to perform a simple handshake
    /// to determine when the child process is ready to accept invocations. This is agnostic to the mechanism that
    /// derived classes use to actually perform the invocations (e.g., they could use HTTP-RPC, or a binary TCP
    /// protocol, or any other RPC-type mechanism).
    /// </summary>
    /// <seealso cref="INodeInstance" />
    internal abstract class OutOfProcessNodeInstance : INodeInstance {
        /// <summary>
        /// The <see cref="ILogger"/> to which the Node.js instance's stdout/stderr is being redirected.
        /// </summary>
        protected readonly ILogger OutputLogger;

        private const string ConnectionEstablishedMessage = "[Microsoft.AspNetCore.NodeServices:Listening]";
        private readonly TaskCompletionSource<object> _connectionIsReadySource = new TaskCompletionSource<object>();
        private bool _disposed;
        private readonly StringAsTempFile _entryPointScript;
        private FileSystemWatcher _fileSystemWatcher;
        private int _invocationTimeoutMilliseconds;
        private bool _launchWithDebugging;
        private readonly Process _nodeProcess;
        private int? _nodeDebuggingPort;
        private bool _nodeProcessNeedsRestart;
        private readonly string[] _watchFileExtensions;

        /// <summary>
        /// Creates a new instance of <see cref="OutOfProcessNodeInstance"/>.
        /// </summary>
        /// <param name="entryPointScript">The path to the entry point script that the Node instance should load and execute.</param>
        /// <param name="projectPath">The root path of the current project. This is used when resolving Node.js module paths relative to the project root.</param>
        /// <param name="watchFileExtensions">The filename extensions that should be watched within the project root. The Node instance will automatically shut itself down if any matching file changes.</param>
        /// <param name="commandLineArguments">Additional command-line arguments to be passed to the Node.js instance.</param>
        /// <param name="applicationStoppingToken">A token that indicates when the host application is stopping.</param>
        /// <param name="nodeOutputLogger">The <see cref="ILogger"/> to which the Node.js instance's stdout/stderr (and other log information) should be written.</param>
        /// <param name="environmentVars">Environment variables to be set on the Node.js process.</param>
        /// <param name="invocationTimeoutMilliseconds">The maximum duration, in milliseconds, to wait for RPC calls to complete.</param>
        /// <param name="launchWithDebugging">If true, passes a flag to the Node.js process telling it to accept V8 debugger connections.</param>
        /// <param name="debuggingPort">If debugging is enabled, the Node.js process should listen for V8 debugger connections on this port.</param>
        public OutOfProcessNodeInstance(
            string entryPointScript,
            string projectPath,
            string[] watchFileExtensions,
            string commandLineArguments,
            CancellationToken applicationStoppingToken,
            ILogger nodeOutputLogger,
            IDictionary<string, string> environmentVars,
            int invocationTimeoutMilliseconds,
            bool launchWithDebugging,
            int debuggingPort) {
            if (nodeOutputLogger == null) {
                throw new ArgumentNullException(nameof(nodeOutputLogger));
            }

            this.OutputLogger = nodeOutputLogger;
            this._entryPointScript = new StringAsTempFile(entryPointScript, applicationStoppingToken);
            this._invocationTimeoutMilliseconds = invocationTimeoutMilliseconds;
            this._launchWithDebugging = launchWithDebugging;

            var startInfo = this.PrepareNodeProcessStartInfo(this._entryPointScript.FileName, projectPath, commandLineArguments,
                environmentVars, this._launchWithDebugging, debuggingPort);
            this._nodeProcess = LaunchNodeProcess(startInfo);
            this._watchFileExtensions = watchFileExtensions;
            this._fileSystemWatcher = this.BeginFileWatcher(projectPath);
            this.ConnectToInputOutputStreams();
        }

        /// <summary>
        /// Asynchronously invokes code in the Node.js instance.
        /// </summary>
        /// <typeparam name="T">The JSON-serializable data type that the Node.js code will asynchronously return.</typeparam>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the invocation.</param>
        /// <param name="moduleName">The path to the Node.js module (i.e., JavaScript file) relative to your project root that contains the code to be invoked.</param>
        /// <param name="exportNameOrNull">If set, specifies the CommonJS export to be invoked. If not set, the module's default CommonJS export itself must be a function to be invoked.</param>
        /// <param name="args">Any sequence of JSON-serializable arguments to be passed to the Node.js function.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the completion of the RPC call.</returns>
        public async Task<T> InvokeExportAsync<T>(
            CancellationToken cancellationToken, string moduleName, string exportNameOrNull, params object[] args) {
            if (this._nodeProcess.HasExited || this._nodeProcessNeedsRestart) {
                // This special kind of exception triggers a transparent retry - NodeServicesImpl will launch
                // a new Node instance and pass the invocation to that one instead.
                // Note that if the Node process is listening for debugger connections, then we need it to shut
                // down immediately and not stay open for connection draining (because if it did, the new Node
                // instance wouldn't able to start, because the old one would still hold the debugging port).
                var message = this._nodeProcess.HasExited
                    ? "The Node process has exited"
                    : "The Node process needs to restart";
                throw new NodeInvocationException(
                    message,
                    details: null,
                    nodeInstanceUnavailable: true,
                    allowConnectionDraining: !this._launchWithDebugging);
            }

            // Construct a new cancellation token that combines the supplied token with the configured invocation
            // timeout. Technically we could avoid wrapping the cancellationToken if no timeout is configured,
            // but that's not really a major use case, since timeouts are enabled by default.
            using (var timeoutSource = new CancellationTokenSource())
            using (var combinedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token)) {
                if (this._invocationTimeoutMilliseconds > 0) {
                    timeoutSource.CancelAfter(this._invocationTimeoutMilliseconds);
                }

                // By overwriting the supplied cancellation token, we ensure that it isn't accidentally used
                // below. We only want to pass through the token that respects timeouts.
                cancellationToken = combinedCancellationTokenSource.Token;
                var connectionDidSucceed = false;

                try {
                    // Wait until the connection is established. This will throw if the connection fails to initialize,
                    // or if cancellation is requested first. Note that we can't really cancel the "establishing connection"
                    // task because that's shared with all callers, but we can stop waiting for it if this call is cancelled.
                    await this._connectionIsReadySource.Task.OrThrowOnCancellation(cancellationToken);
                    connectionDidSucceed = true;

                    return await this.InvokeExportAsync<T>(new NodeInvocationInfo {
                        ModuleName = moduleName,
                        ExportedFunctionName = exportNameOrNull,
                        Args = args
                    }, cancellationToken);
                } catch (TaskCanceledException) {
                    if (timeoutSource.IsCancellationRequested) {
                        // It was very common for developers to report 'TaskCanceledException' when encountering almost any
                        // trouble when using NodeServices. Now we have a default invocation timeout, and attempt to give
                        // a more descriptive exception message if it happens.
                        if (!connectionDidSucceed) {
                            // This is very unlikely, but for debugging, it's still useful to differentiate it from the
                            // case below.
                            throw new NodeInvocationException(
                                $"Attempt to connect to Node timed out after {this._invocationTimeoutMilliseconds}ms.",
                                string.Empty);
                        } else {
                            // Developers encounter this fairly often (if their Node code fails without invoking the callback,
                            // all that the .NET side knows is that the invocation eventually times out). Previously, this surfaced
                            // as a TaskCanceledException, but this led to a lot of issue reports. Now we throw the following
                            // descriptive error.
                            throw new NodeInvocationException(
                                $"The Node invocation timed out after {this._invocationTimeoutMilliseconds}ms.",
                                $"You can change the timeout duration by setting the {NodeServicesOptions.TimeoutConfigPropertyName} "
                                + $"property on {nameof(NodeServicesOptions)}.\n\n"
                                + "The first debugging step is to ensure that your Node.js function always invokes the supplied "
                                + "callback (or throws an exception synchronously), even if it encounters an error. Otherwise, "
                                + "the .NET code has no way to know that it is finished or has failed."
                            );
                        }
                    } else {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose() {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously invokes code in the Node.js instance.
        /// </summary>
        /// <typeparam name="T">The JSON-serializable data type that the Node.js code will asynchronously return.</typeparam>
        /// <param name="invocationInfo">Specifies the Node.js function to be invoked and arguments to be passed to it.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the invocation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the completion of the RPC call.</returns>
        protected abstract Task<T> InvokeExportAsync<T>(
            NodeInvocationInfo invocationInfo,
            CancellationToken cancellationToken);

        /// <summary>
        /// Configures a <see cref="ProcessStartInfo"/> instance describing how to launch the Node.js process.
        /// </summary>
        /// <param name="entryPointFilename">The entrypoint JavaScript file that the Node.js process should execute.</param>
        /// <param name="projectPath">The root path of the project. This is used when locating Node.js modules relative to the project root.</param>
        /// <param name="commandLineArguments">Command-line arguments to be passed to the Node.js process.</param>
        /// <param name="environmentVars">Environment variables to be set on the Node.js process.</param>
        /// <param name="launchWithDebugging">If true, passes a flag to the Node.js process telling it to accept V8 Inspector connections.</param>
        /// <param name="debuggingPort">If debugging is enabled, the Node.js process should listen for V8 Inspector connections on this port.</param>
        /// <returns></returns>
        protected virtual ProcessStartInfo PrepareNodeProcessStartInfo(
            string entryPointFilename, string projectPath, string commandLineArguments,
            IDictionary<string, string> environmentVars, bool launchWithDebugging, int debuggingPort) {
            // This method is virtual, as it provides a way to override the NODE_PATH or the path to node.exe
            string debuggingArgs;
            if (launchWithDebugging) {
                debuggingArgs = debuggingPort != default(int) ? $"--inspect={debuggingPort} " : "--inspect ";
                this._nodeDebuggingPort = debuggingPort;
            } else {
                debuggingArgs = string.Empty;
            }

            var thisProcessPid = Process.GetCurrentProcess().Id;
            var startInfo = new ProcessStartInfo("node") {
                Arguments = $"{debuggingArgs}\"{entryPointFilename}\" --parentPid {thisProcessPid} {commandLineArguments ?? string.Empty}",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = projectPath
            };

            // Append environment vars
            if (environmentVars != null) {
                foreach (var envVarKey in environmentVars.Keys) {
                    var envVarValue = environmentVars[envVarKey];
                    if (envVarValue != null) {
                        SetEnvironmentVariable(startInfo, envVarKey, envVarValue);
                    }
                }
            }

            // Append projectPath to NODE_PATH so it can locate node_modules
            var existingNodePath = Environment.GetEnvironmentVariable("NODE_PATH") ?? string.Empty;
            if (existingNodePath != string.Empty) {
                existingNodePath += Path.PathSeparator;
            }

            var nodePathValue = existingNodePath + Path.Combine(projectPath, "node_modules");
            SetEnvironmentVariable(startInfo, "NODE_PATH", nodePathValue);

            return startInfo;
        }

        /// <summary>
        /// Virtual method invoked whenever the Node.js process emits a line to its stdout.
        /// </summary>
        /// <param name="outputData">The line emitted to the Node.js process's stdout.</param>
        protected virtual void OnOutputDataReceived(string outputData) {
            this.OutputLogger.LogInformation(outputData);
        }

        /// <summary>
        /// Virtual method invoked whenever the Node.js process emits a line to its stderr.
        /// </summary>
        /// <param name="errorData">The line emitted to the Node.js process's stderr.</param>
        protected virtual void OnErrorDataReceived(string errorData) {
            this.OutputLogger.LogError(errorData);
        }

        /// <summary>
        /// Disposes the instance.
        /// </summary>
        /// <param name="disposing">True if the object is disposing or false if it is finalizing.</param>
        protected virtual void Dispose(bool disposing) {
            if (!this._disposed) {
                if (disposing) {
                    this._entryPointScript.Dispose();
                    this.EnsureFileSystemWatcherIsDisposed();
                }

                // Make sure the Node process is finished
                // TODO: Is there a more graceful way to end it? Or does this still let it perform any cleanup?
                if (this._nodeProcess != null && !this._nodeProcess.HasExited) {
                    this._nodeProcess.Kill();
                }

                this._disposed = true;
            }
        }

        private void EnsureFileSystemWatcherIsDisposed() {
            if (this._fileSystemWatcher != null) {
                this._fileSystemWatcher.Dispose();
                this._fileSystemWatcher = null;
            }
        }

        private static void SetEnvironmentVariable(ProcessStartInfo startInfo, string name, string value) {
            startInfo.EnvironmentVariables[name] = value;
        }

        private static Process LaunchNodeProcess(ProcessStartInfo startInfo) {
            try {
                var process = Process.Start(startInfo);

                // On Mac at least, a killed child process is left open as a zombie until the parent
                // captures its exit code. We don't need the exit code for this process, and don't want
                // to use process.WaitForExit() explicitly (we'd have to block the thread until it really
                // has exited), but we don't want to leave zombies lying around either. It's sufficient
                // to use process.EnableRaisingEvents so that .NET will grab the exit code and let the
                // zombie be cleaned away without having to block our thread.
                process.EnableRaisingEvents = true;

                return process;
            } catch (Exception ex) {
                var message = "Failed to start Node process. To resolve this:.\n\n"
                            + "[1] Ensure that Node.js is installed and can be found in one of the PATH directories.\n"
                            + $"    Current PATH enviroment variable is: { Environment.GetEnvironmentVariable("PATH") }\n"
                            + "    Make sure the Node executable is in one of those directories, or update your PATH.\n\n"
                            + "[2] See the InnerException for further details of the cause.";
                throw new InvalidOperationException(message, ex);
            }
        }

        private static string UnencodeNewlines(string str) {
            if (str != null) {
                // The token here needs to match the const in OverrideStdOutputs.ts.
                // See the comment there for why we're doing this.
                str = str.Replace("__ns_newline__", Environment.NewLine);
            }

            return str;
        }

        private void ConnectToInputOutputStreams() {
            var initializationIsCompleted = false;

            this._nodeProcess.OutputDataReceived += (sender, evt) => {
                if (evt.Data == ConnectionEstablishedMessage && !initializationIsCompleted) {
                    this._connectionIsReadySource.SetResult(null);
                    initializationIsCompleted = true;
                } else if (evt.Data != null) {
                    this.OnOutputDataReceived(UnencodeNewlines(evt.Data));
                }
            };

            this._nodeProcess.ErrorDataReceived += (sender, evt) => {
                if (evt.Data != null) {
                    if (this._launchWithDebugging && IsDebuggerMessage(evt.Data)) {
                        this.OutputLogger.LogWarning(evt.Data);
                    } else {
                        this.OnErrorDataReceived(UnencodeNewlines(evt.Data));
                    }
                }
            };

            this._nodeProcess.BeginOutputReadLine();
            this._nodeProcess.BeginErrorReadLine();
        }

        private static bool IsDebuggerMessage(string message) {
            return message.StartsWith("Debugger attached", StringComparison.Ordinal) ||
                message.StartsWith("Debugger listening ", StringComparison.Ordinal) ||
                message.StartsWith("To start debugging", StringComparison.Ordinal) ||
                message.Equals("Warning: This is an experimental feature and could change at any time.", StringComparison.Ordinal) ||
                message.Equals("For help see https://nodejs.org/en/docs/inspector", StringComparison.Ordinal) ||
                message.Contains("chrome-devtools:");
        }

        private FileSystemWatcher BeginFileWatcher(string rootDir) {
            if (this._watchFileExtensions == null || this._watchFileExtensions.Length == 0) {
                // Nothing to watch
                return null;
            }

            var watcher = new FileSystemWatcher(rootDir) {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            watcher.Changed += this.OnFileChanged;
            watcher.Created += this.OnFileChanged;
            watcher.Deleted += this.OnFileChanged;
            watcher.Renamed += this.OnFileRenamed;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void OnFileChanged(object source, FileSystemEventArgs e) {
            if (this.IsFilenameBeingWatched(e.FullPath)) {
                this.RestartDueToFileChange(e.FullPath);
            }
        }

        private void OnFileRenamed(object source, RenamedEventArgs e) {
            if (this.IsFilenameBeingWatched(e.OldFullPath) || this.IsFilenameBeingWatched(e.FullPath)) {
                this.RestartDueToFileChange(e.OldFullPath);
            }
        }

        private bool IsFilenameBeingWatched(string fullPath) {
            if (string.IsNullOrEmpty(fullPath)) {
                return false;
            } else {
                var actualExtension = Path.GetExtension(fullPath) ?? string.Empty;
                return this._watchFileExtensions.Any(actualExtension.Equals);
            }
        }

        private void RestartDueToFileChange(string fullPath) {
            this.OutputLogger.LogInformation($"Node will restart because file changed: {fullPath}");

            this._nodeProcessNeedsRestart = true;

            // There's no need to watch for any more changes, since we're already restarting, and if the
            // restart takes some time (e.g., due to connection draining), we could end up getting duplicate
            // notifications.
            this.EnsureFileSystemWatcherIsDisposed();
        }

        /// <summary>
        /// Implements the finalization part of the IDisposable pattern by calling Dispose(false).
        /// </summary>
        ~OutOfProcessNodeInstance() {
            this.Dispose(false);
        }
    }
}
