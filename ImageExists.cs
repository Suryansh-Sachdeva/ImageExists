using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageExists
{
    public class ImageExists
    {
        public string TemplatePath { get; set; }

        public bool Result { get; private set; }

        private const int ProcessTimeoutMilliseconds = 30000;

        // Prevent multiple executions from interfering with each other.
        private static readonly SemaphoreSlim ExecutionLock =
            new SemaphoreSlim(1, 1);

        public async Task ExecuteAsync()
        {
            await ExecutionLock.WaitAsync();

            string screenshotPath = null;

            try
            {
                Result = false;

                // 1. Validate the template image.

                EnsureFileExists(TemplatePath, "Template image");

                // 2. Locate the required files.

                string projectDirectory =
                    AppDomain.CurrentDomain.BaseDirectory;

                string screenshotExecutable = Path.Combine(
                    projectDirectory,
                    "Screenshot.exe"
                );

                string matcherPath = Path.Combine(
                    projectDirectory,
                    "matcher.py"
                );

                EnsureFileExists(
                    screenshotExecutable,
                    "Screenshot.exe"
                );

                EnsureFileExists(
                    matcherPath,
                    "matcher.py"
                );

                // Use a unique filename for each screenshot.

                screenshotPath = Path.Combine(
                    projectDirectory,
                    "screenshot_" + Guid.NewGuid().ToString("N") + ".png"
                );

                // 3. Capture the screenshot.

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

                // 4. Run Python image matching.

                ProcessStartInfo matcherInfo =
                    new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments =
                            QuoteArgument(matcherPath) + " " +
                            QuoteArgument(screenshotPath) + " " +
                            QuoteArgument(TemplatePath),

                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                ProcessResult matcherResult =
                    await RunProcessAsync(matcherInfo);

                if (matcherResult.ExitCode != 0)
                {
                    throw new Exception(
                        "Python matcher failed: " +
                        matcherResult.Error
                    );
                }

                // 5. Read Python's result.

                string output = matcherResult.Output.Trim();

                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new Exception(
                        "Python matcher returned no result."
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
                // 6. Always attempt screenshot cleanup.

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

                    await waitTask;

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

        // Quotes command-line arguments safely.

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
