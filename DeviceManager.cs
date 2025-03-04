using System.IO.Ports;
using System.Diagnostics;

namespace ZeDMDUpdater;

public class DeviceManager
{
    public async Task<bool> FlashFirmware(string firmwarePath, string portName, bool isS3 = false, Action<string>? logCallback = null)
    {
        try
        {
            // Determine chip type
            string chipType = isS3 ? "esp32s3" : "esp32";

            // Construct the command
            string command = OperatingSystem.IsWindows() ? "esptool.exe" : "esptool";
            string arguments = $"--chip {chipType} --port {portName} write_flash 0x0 \"{firmwarePath}\"";

            // Create process start info
            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Create and start the process
            using (var process = new Process())
            {
                process.StartInfo = processStartInfo;

                // Handle output and error streams
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        logCallback?.Invoke(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        logCallback?.Invoke($"Error: {e.Data}");
                    }
                };

                // Start the process
                process.Start();

                // Begin reading output and error streams
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for the process to complete
                await process.WaitForExitAsync();

                // Return true if the process exited successfully
                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Error: {ex.Message}");
            return false;
        }
    }

}
