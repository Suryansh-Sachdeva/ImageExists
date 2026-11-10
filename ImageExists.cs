using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageExists
{
    public class ImageExists
    {
        // Register cleanup when the application shuts down.

        static ImageExists()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                StopMatcher();
            };
        }
        public string TemplatePath { get; set; }

        public bool Result { get; private set; }

        private const int ProcessTimeoutMilliseconds = 30000;

        // Prevent multiple executions from interfering with each other.
        private static readonly SemaphoreSlim ExecutionLock =
            new SemaphoreSlim(1, 1);

        // Persistent Python matcher process.
        private static Process matcherProcess;

        private static StreamWriter matcherInput;

        private static StreamReader matcherOutput;

        public async Task ExecuteAsync()
        {
            await ExecutionLock.WaitAsync();

            string screenshotPath = null;

            try
            {
                Result = false;

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

                EnsureFileExists(TemplatePath, "Template image");
                EnsureFileExists(screenshotExecutable, "Screenshot.exe");
                EnsureFileExists(matcherPath, "matcher.py");
                EnsureMatcherRunning(matcherPath);

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
                    await RunProcessAsync(screenshotInfo);

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

                await matcherInput.WriteLineAsync(screenshotPath);
                await matcherInput.WriteLineAsync(TemplatePath);
                await matcherInput.FlushAsync();

                // 6. Read Python's result.

                Task<string> responseTask = matcherOutput.ReadLineAsync();

                Task timeoutTask = Task.Delay(
                    ProcessTimeoutMilliseconds
                );

                Task completedTask = await Task.WhenAny(
                    responseTask,
                    timeoutTask
                );

                if (completedTask != responseTask)
                {
                    StopMatcher();

                    throw new TimeoutException(
                        "Python matcher did not respond in time."
                    );
                }

                string output = await responseTask;

                if (output == null)
                {
                    throw new Exception(
                        "Python matcher stopped unexpectedly."
                    );
                }

                if (output.Equals(
                    "true",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Result = true;
                }
                else if (output.Equals(
                    "false",
                    StringComparison.OrdinalIgnoreCase))
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

        private static void StartMatcher(string matcherPath)
        {
            ProcessStartInfo matcherInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData
                    ),
                    @"Programs\Python\Python314\python.exe"
                ),
                Arguments = QuoteArgument(matcherPath),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            matcherProcess = new Process();
            matcherProcess.StartInfo = matcherInfo;

            if (!matcherProcess.Start())
            {
                matcherProcess.Dispose();
                matcherProcess = null;

                throw new Exception("Could not start Python matcher.");
            }

            matcherInput = matcherProcess.StandardInput;
            matcherOutput = matcherProcess.StandardOutput;

            // Keep a local reference to this process.
            Process process = matcherProcess;

            // Continuously drain Python's error output.
            Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardError.ReadLineAsync() != null)
                    {
                        // Error output is consumed to prevent the pipe from filling.
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Process streams have been closed.
                }
            });
        }

        // Ensures that the Python matcher is running.

        private static void EnsureMatcherRunning(string matcherPath)
        {
            if (matcherProcess != null)
            {
                if (!matcherProcess.HasExited)
                {
                    return;
                }

                matcherProcess.Dispose();

                matcherProcess = null;
                matcherInput = null;
                matcherOutput = null;
            }

            StartMatcher(matcherPath);
        }

        // Stops the persistent Python matcher process.

        private static void StopMatcher()
        {
            if (matcherProcess == null)
            {
                return;
            }

            try
            {
                if (!matcherProcess.HasExited)
                {
                    matcherProcess.Kill();
                    matcherProcess.WaitForExit();
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
                matcherProcess.Dispose();

                matcherProcess = null;
                matcherInput = null;
                matcherOutput = null;
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

        private static async Task<ProcessResult> RunProcessAsync(
            ProcessStartInfo startInfo)
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

                // Read stdout and stderr concurrently.

                Task<string> outputTask =
                    process.StandardOutput.ReadToEndAsync();

                Task<string> errorTask =
                    process.StandardError.ReadToEndAsync();

                // Wait asynchronously for process completion.

                Task waitTask = Task.Run(
                    () => process.WaitForExit()
                );

                Task timeoutTask = Task.Delay(
                    ProcessTimeoutMilliseconds
                );

                Task completedTask = await Task.WhenAny(
                    waitTask,
                    timeoutTask
                );

                if (completedTask != waitTask)
                {
                    try
                    {
                        process.Kill();
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

                await waitTask;

                await Task.WhenAll(
                    outputTask,
                    errorTask
                );

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