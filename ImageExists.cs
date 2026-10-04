using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ImageExists
{
    public class ImageExists
    {
        public string TemplatePath { get; set; }

        public bool Result { get; private set; }

        public async Task ExecuteAsync()
        {
            if (string.IsNullOrWhiteSpace(TemplatePath))
            {
                throw new ArgumentException("TemplatePath cannot be empty.");
            }

            if (!File.Exists(TemplatePath))
            {
                throw new FileNotFoundException(
                    "Template image was not found.",
                    TemplatePath
                );
            }

            string projectDirectory =
                AppDomain.CurrentDomain.BaseDirectory;

            string screenshotPath =
                Path.Combine(projectDirectory, "screenshot.png");

            string matcherPath =
                Path.Combine(projectDirectory, "matcher.py");

            if (!File.Exists(matcherPath))
            {
                throw new FileNotFoundException(
                    "matcher.py was not found.",
                    matcherPath
                );
            }

            // ---------------------------------------------------------
            // 1. Take a screenshot using the external screenshot tool
            // ---------------------------------------------------------

            string screenshotExecutable =
                Path.Combine(projectDirectory, "Screenshot.exe");

            if (!File.Exists(screenshotExecutable))
            {
                throw new FileNotFoundException(
                    "Screenshot.exe was not found.",
                    screenshotExecutable
                );
            }

            var screenshotProcess = new Process();

            screenshotProcess.StartInfo = new ProcessStartInfo
            {
                FileName = screenshotExecutable,
                Arguments = $"\"{screenshotPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            screenshotProcess.Start();

            string screenshotOutput =
                await screenshotProcess.StandardOutput.ReadToEndAsync();

            string screenshotError =
                await screenshotProcess.StandardError.ReadToEndAsync();

            screenshotProcess.WaitForExit();

            if (screenshotProcess.ExitCode != 0)
            {
                throw new Exception(
                    "Screenshot tool failed: " + screenshotError
                );
            }

            if (!File.Exists(screenshotPath))
            {
                throw new Exception(
                    "Screenshot was not created."
                );
            }

            // ---------------------------------------------------------
            // 2. Run Python/OpenCV image matching
            // ---------------------------------------------------------

            var matcherProcess = new Process();

            matcherProcess.StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments =
                    $"\"{matcherPath}\" " +
                    $"\"{screenshotPath}\" " +
                    $"\"{TemplatePath}\"",

                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            matcherProcess.Start();

            string output =
                await matcherProcess.StandardOutput.ReadToEndAsync();

            string error =
                await matcherProcess.StandardError.ReadToEndAsync();

            screenshotProcess.WaitForExit();

            if (matcherProcess.ExitCode != 0)
            {
                throw new Exception(
                    "Python matcher failed: " + error
                );
            }

            // ---------------------------------------------------------
            // 3. Read the result returned by matcher.py
            // ---------------------------------------------------------

            output = output.Trim();

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new Exception(
                    "Python matcher returned no result."
                );
            }

            Result = output.Equals(
                "true",
                StringComparison.OrdinalIgnoreCase
            );

            // ---------------------------------------------------------
            // 4. Clean up temporary screenshot
            // ---------------------------------------------------------

            try
            {
                if (File.Exists(screenshotPath))
                {
                    File.Delete(screenshotPath);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }
}
