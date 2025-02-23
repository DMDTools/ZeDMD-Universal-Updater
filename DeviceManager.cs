using System.IO.Ports;
using System.Diagnostics;

namespace ZeDMDUpdater;

public class DeviceManager
{
    public Task<List<string>> GetAvailablePorts()
    {
        // TODO: Use LibZeDMD to list ZeDMD devices
        List<string> ports = new List<string>();

        // Standard SerialPort.GetPortNames() attempt
        ports.AddRange(SerialPort.GetPortNames());

        // Linux-specific checks
        if (OperatingSystem.IsLinux())
        {
            // Check for ttyUSB devices
            string[] ttyUSBDevices = Directory.GetFiles("/dev", "ttyUSB*");
            ports.AddRange(ttyUSBDevices);

            // Check for ttyACM devices (for Arduino-compatible devices)
            string[] ttyACMDevices = Directory.GetFiles("/dev", "ttyACM*");
            ports.AddRange(ttyACMDevices);

            // Check for ttyS devices
            string[] ttySDevices = Directory.GetFiles("/dev", "ttyS*");
            ports.AddRange(ttySDevices);
        }

        // Remove duplicates and sort
        return Task.FromResult(ports.Distinct().OrderBy(p => p).ToList());
    }

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


    public Task ApplySettings(Dictionary<string, string> settings)
    {
        // Implement settings application logic here
        return Task.CompletedTask;
    }
}
