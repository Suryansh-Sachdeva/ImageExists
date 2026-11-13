using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using LTools.Common.UIElements;
using LTools.SDK;
using LTools.Common.Model;
using System.Collections.Generic;
using LTools.Common.Helpers;
using LTools.Common.Model.Studio;

namespace ImageExists
{
    public class ImageExists : PrimoComponentSimple<ImageExistsUI>
    {
        public override string GroupName { get; protected set; }
        public override ExecutionResult SimpleAction(ScriptingData sd)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ResultVariable))
                {
                    throw new Exception(
                        "Please select a Boolean output variable."
                    );
                }

                ExecuteAsync(sd).GetAwaiter().GetResult();
                SetVariableValue(
                    ResultVariable,
                    Result,
                    typeof(bool),
                    sd);

                return new ExecutionResult
                {
                    IsSuccess = true,
                    SuccessMessage = "ImageExists executed successfully"
                };
            }
            catch (Exception ex)
            {
                return new ExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        public ImageExists(IWFContainer container) : base(container)
        {
            InitClass(container);
        }
        // Register cleanup when the application shuts down.

        static ImageExists()
        {
            sdkComponentName = "Image Exists";
            sdkComponentIcon = "pack://application:,,,/Primo.ImageExists;component/Images/ImageExists.png";
            sdkComponentHelp = "Checks whether a specified image is visible on the screen.";
            sdkComponentHelpUrl = "";

            sdkProperties = new List<WFHelper.PropertiesItem>
            {
                new WFHelper.PropertiesItem
                {
                    PropName = nameof(TemplatePath),
                    ToolTip = "Path to the template image to search for.",
                    PropertyType = WFHelper.PropertiesItem.PropertyTypes.SCRIPT,
                    EditorType = ScriptEditorTypes.FILE_SELECTOR,
                    DataType = typeof(string)
                },
                new WFHelper.PropertiesItem
                {
                    PropName = nameof(ResultVariable),
                    ToolTip = "Select the Boolean workflow variable to store the result.",
                    PropertyType = WFHelper.PropertiesItem.PropertyTypes.VARIABLE,
                    DataType = typeof(bool)
                }
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                StopMatcher();
            };
        }
        public string TemplatePath { get; set; }

        public bool Result { get; private set; }
        [ValidateReturnScript(DataType = typeof(bool))]
        public string ResultVariable { get; set; }

        private const int ProcessTimeoutMilliseconds = 30000;

        // Prevent multiple executions from interfering with each other.
        private static readonly SemaphoreSlim ExecutionLock =
            new SemaphoreSlim(1, 1);

        // Persistent Python matcher process.
        private static Process matcherProcess;

        private static StreamWriter matcherInput;

        private static StreamReader matcherOutput;

        public async Task ExecuteAsync(ScriptingData sd)
        {
            await ExecutionLock.WaitAsync().ConfigureAwait(false);

            string screenshotPath = null;

            try
            {
                Result = false;

                string resolvedTemplatePath = GetPropertyValue(
                    TemplatePath,
                    nameof(TemplatePath),
                    sd) as string;

                if (string.IsNullOrWhiteSpace(resolvedTemplatePath))
                {
                    throw new Exception("Template image path is empty or invalid.");
                }

                // 1. Locate the required files.

                string projectDirectory =
                    AppDomain.CurrentDomain.BaseDirectory;

                string screenshotExecutable = Path.Combine(
                    projectDirectory,
                    "ScreenshotTool.exe"
                );

                string matcherPath = Path.Combine(
                    projectDirectory,
                    "matcher.py"
                );

                // 2. Validate the required files.

                EnsureFileExists(resolvedTemplatePath, "Template image");
                EnsureFileExists(screenshotExecutable, "ScreenshotTool.exe");
                EnsureFileExists(matcherPath, "matcher.py");
                await EnsureMatcherRunning(matcherPath).ConfigureAwait(false);

                // 3. Generate a unique screenshot filename.

                screenshotPath = Path.Combine(
                    Path.GetTempPath(),
                    "screenshot_" + Guid.NewGuid().ToString("N") + ".png"
                );

                // 4. Capture the current screen.

                ProcessStartInfo screenshotInfo =
                    new ProcessStartInfo
                    {
                        FileName = screenshotExecutable,
                        Arguments = QuoteArgument(screenshotPath),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                ProcessResult screenshotResult =
                    await RunProcessAsync(screenshotInfo).ConfigureAwait(false);

                if (screenshotResult.ExitCode != 0)
                {
                    throw new Exception(
                        "Screenshot tool failed: " +
                        screenshotResult.Error
                    );
                }

                EnsureFileExists(
                    screenshotPath,
                    "Captured screenshot"
                );

                // 5. Send the matching request to the persistent Python process.

                try
                {
                    await matcherInput.WriteLineAsync(screenshotPath)
                        .ConfigureAwait(false);

                    await matcherInput.WriteLineAsync(resolvedTemplatePath)
                        .ConfigureAwait(false);

                    await matcherInput.FlushAsync().ConfigureAwait(false);

                    // 6. Read Python's result.

                    Task<string> responseTask = matcherOutput.ReadLineAsync();

                    Task timeoutTask = Task.Delay(
                        ProcessTimeoutMilliseconds
                    );

                    Task completedTask = await Task.WhenAny(
                        responseTask,
                        timeoutTask
                    ).ConfigureAwait(false);

                    if (completedTask != responseTask)
                    {
                        throw new TimeoutException(
                            "Python matcher did not respond in time."
                        );
                    }

                    string output = await responseTask.ConfigureAwait(false);

                    if (output == null)
                    {
                        throw new Exception(
                            "Python matcher stopped unexpectedly."
                        );
                    }

                    if (output.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        Result = true;
                    }
                    else if (output.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        Result = false;
                    }
                    else
                    {
                        throw new Exception(
                            "Unexpected Python output: " + output
                        );
                    }
                }
                catch
                {
                    // Stop a failed matcher so it can be restarted next time.

                    StopMatcher();

                    throw;
                }
            }
            finally
            {
                // 7. Always attempt screenshot cleanup.

                if (screenshotPath != null)
                {
                    try
                    {
                        if (File.Exists(screenshotPath))
                        {
                            File.Delete(screenshotPath);
                        }
                    }
                    catch (IOException)
                    {
                        // Do not hide the original error.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Do not hide the original error.
                    }
                }

                ExecutionLock.Release();
            }
        }

        // Starts the persistent Python matcher process.

        private static async Task StartMatcher(string matcherPath)
        {
            string pythonPath = "py";

            ProcessStartInfo matcherInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-3 " + QuoteArgument(matcherPath),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = new Process
            {
                StartInfo = matcherInfo
            };

            try
            {
                if (!process.Start())
                {
                    throw new Exception(
                        "Python process could not be started."
                    );
                }

                // Wait for Python to confirm that its dependencies loaded.
                Task<string> startupTask =
                    process.StandardOutput.ReadLineAsync();

                Task timeoutTask = Task.Delay(10000);

                Task completedTask = await Task.WhenAny(
                    startupTask,
                    timeoutTask
                ).ConfigureAwait(false);

                if (completedTask != startupTask)
                {
                    throw new TimeoutException(
                        "Python matcher did not complete startup within 10 seconds."
                    );
                }

                string startupMessage =
                    await startupTask.ConfigureAwait(false);

                if (startupMessage != "READY")
                {
                    throw new Exception(
                        startupMessage ??
                        "Python matcher exited before completing startup."
                    );
                }

                // Register the process only after successful startup.
                matcherProcess = process;
                matcherInput = process.StandardInput;
                matcherOutput = process.StandardOutput;

                // Continuously drain Python's error output.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (await process.StandardError.ReadLineAsync()
                            .ConfigureAwait(false) != null)
                        {
                            // Consume error output to prevent the pipe from filling.
                        }
                    }
                    catch (IOException)
                    {
                        // Error stream was closed.
                    }
                    catch (ObjectDisposedException)
                    {
                        // Process streams have been disposed.
                    }
                    catch (InvalidOperationException)
                    {
                        // Process is no longer available.
                    }
                });
            }
            catch (Exception ex)
            {
                matcherProcess = null;
                matcherInput = null;
                matcherOutput = null;

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process may already have exited.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process could not be terminated.
                }
                finally
                {
                    process.Dispose();
                }

                throw new Exception(
                    "Could not start Python matcher: " + ex.Message +
                    " Check that Python 3, OpenCV and NumPy are installed.",
                    ex
                );
            }
        }

        // Ensures that the Python matcher is running.

        private static async Task EnsureMatcherRunning(string matcherPath)
        {
            if (matcherProcess != null)
            {
                try
                {
                    if (!matcherProcess.HasExited)
                    {
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process is no longer available.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Could not retrieve process information.
                }

                // Clean up the previous process.
                try
                {
                    matcherProcess.Dispose();
                }
                catch (InvalidOperationException)
                {
                    // Process may already be disposed.
                }

                matcherProcess = null;
                matcherInput = null;
                matcherOutput = null;
            }

            await StartMatcher(matcherPath).ConfigureAwait(false);
        }

        // Stops the persistent Python matcher process.

        private static void StopMatcher()
        {
            Process process = matcherProcess;

            if (process == null)
            {
                return;
            }

            matcherProcess = null;

            StreamWriter input = matcherInput;
            StreamReader output = matcherOutput;

            matcherInput = null;
            matcherOutput = null;

            try
            {
                // Tell Python that no more requests will be sent.
                if (input != null)
                {
                    try
                    {
                        input.Close();
                    }
                    catch (IOException)
                    {
                        // Input stream may already be closed.
                    }
                    catch (InvalidOperationException)
                    {
                        // Input stream may already be disposed.
                    }
                }

                // Allow Python to exit normally.
                if (!process.HasExited && !process.WaitForExit(2000))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // Process may already have exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Process could not be terminated.
            }
            finally
            {
                if (input != null)
                {
                    input.Dispose();
                }

                if (output != null)
                {
                    output.Dispose();
                }

                process.Dispose();
            }
        }

        // Checks whether a required file exists.

        private static void EnsureFileExists(
            string path,
            string description)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    description + " path cannot be empty."
                );
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    description + " was not found.",
                    path
                );
            }
        }

        // Runs an external process with a timeout.

        private static async Task<ProcessResult> RunProcessAsync(ProcessStartInfo startInfo)
        {
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                if (!process.Start())
                {
                    throw new Exception(
                        "Could not start process: " +
                        startInfo.FileName
                    );
                }

                Task<string> outputTask =
                    process.StandardOutput.ReadToEndAsync();

                Task<string> errorTask =
                    process.StandardError.ReadToEndAsync();

                Task waitTask = Task.Run(
                    () => process.WaitForExit()
                );

                Task timeoutTask = Task.Delay(
                    ProcessTimeoutMilliseconds
                );

                Task completedTask = await Task.WhenAny(
                    waitTask,
                    timeoutTask
                ).ConfigureAwait(false);

                if (completedTask != waitTask)
                {
                    try
                    {
                        process.Kill();

                        await Task.Run(
                            () => process.WaitForExit(5000)
                        ).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // Process may already have exited.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Process could not be terminated.
                    }

                    throw new TimeoutException(
                        "Process timed out: " +
                        startInfo.FileName
                    );
                }

                await waitTask.ConfigureAwait(false);

                await Task.WhenAll(
                    outputTask,
                    errorTask
                ).ConfigureAwait(false);

                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = outputTask.Result,
                    Error = errorTask.Result
                };
            }
        }

        // Quotes command-line arguments.

        private static string QuoteArgument(string argument)
        {
            return "\"" +
                argument.Replace("\"", "\\\"") +
                "\"";
        }

        // Holds the output returned by an external process.

        private class ProcessResult
        {
            public int ExitCode { get; set; }

            public string Output { get; set; }

            public string Error { get; set; }
        }
    }
}