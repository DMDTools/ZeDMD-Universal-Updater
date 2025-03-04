using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Spectre.Console;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO.Ports;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;


namespace ZeDMDUpdater;

internal static class Esp32Devices
{
    public static List<Esp32Device> esp32Devices = new List<Esp32Device>();
    public static Esp32Device wifiDevice = new Esp32Device("", false, false, false);
    public static int[] I2SallowedSpeed = { 8, 16, 20 };
    private readonly static (string chip, bool s3, bool lilygo)[] USBtoSerialDevices = [
        ("CP210x", false, false),
        ("CP2102", false, false),
        ("CH340", false, false),
        ("CH9102", true, true),
        ("CH343", true, false)
    ];

    public static void GetPortNames()
    {
        var portNames = SerialPort.GetPortNames();

        foreach (string portName in portNames)
        {
            AnsiConsole.MarkupLine($"[grey]Scanning port {portName}...[/]");
            string normalizedPortName = portName;
            int portNumber = -1;

            // Handle different OS port naming conventions
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Match match = Regex.Match(portName, @"COM(\d+)");
                if (match.Success)
                {
                    portNumber = int.Parse(match.Groups[1].Value);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Handle Linux serial ports (ttyUSB*, ttyACM*)
                Match match = Regex.Match(portName, @"tty(?:USB|ACM)(\d+)");
                if (match.Success)
                {
                    portNumber = int.Parse(match.Groups[1].Value);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Handle macOS serial ports
                Match match = Regex.Match(portName, @"tty\.(?:usbserial|usbmodem).*");
                if (match.Success)
                {
                    AnsiConsole.MarkupLine($"[grey]Found macOS serial port {portName}[/]");
                    // On macOS, we'll use the index in the ports array as the number
                    portNumber = Array.IndexOf(portNames, portName);
                }
            }

            if (portNumber >= 0)
            {
                // Try to identify the device type using available system information
                foreach (var device in USBtoSerialDevices)
                {
                    // On Unix systems, we can try to read device information from sysfs
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            string devicePath = $"/sys/class/tty/{Path.GetFileName(portName)}/device/driver";

                            if (File.Exists(devicePath))
                            {
                                string driverInfo = File.ReadAllText(devicePath);
                                AnsiConsole.MarkupLine($"[grey]Found device {device.chip}[/]");
                                if (driverInfo.Contains(device.chip, StringComparison.OrdinalIgnoreCase))
                                {
                                    esp32Devices.Add(new Esp32Device(devicePath, device.s3, device.lilygo, false));
                                    break;
                                }
                            }
                        }
                        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            try
                            {
                                var process = new Process
                                {
                                    StartInfo = new ProcessStartInfo
                                    {
                                        FileName = "ioreg",
                                        Arguments = "-r -c IOUSBHostDevice",
                                        RedirectStandardOutput = true,
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    }
                                };

                                process.Start();
                                string output = process.StandardOutput.ReadToEnd();
                                process.WaitForExit();

                                // Split output into device blocks
                                var deviceBlocks = output.Split(new[] { "+-o" }, StringSplitOptions.RemoveEmptyEntries);

                                foreach (var block in deviceBlocks)
                                {
                                    if (block.Contains(device.chip, StringComparison.OrdinalIgnoreCase))
                                    {
                                        AnsiConsole.MarkupLine($"[grey]Found device {device.chip}[/]");
                                        // Extract serial number
                                        var serialMatch = Regex.Match(block, @"""USB Serial Number""\s*=\s*""([^""]+)""");
                                        if (serialMatch.Success)
                                        {
                                            string serialNumber = serialMatch.Groups[1].Value;
                                            // Construct the likely device path
                                            string probableDevicePath = $"/dev/tty.usbserial-{serialNumber}";

                                            // Verify the device path exists
                                            if (File.Exists(probableDevicePath))
                                            {
                                                AnsiConsole.MarkupLine($"[grey]Found {device.chip} device at {probableDevicePath}[/]");
                                                esp32Devices.Add(new Esp32Device(probableDevicePath, device.s3, device.lilygo, false));
                                                break;
                                            }

                                            // Also check for alternative naming pattern
                                            string alternativeDevicePath = $"/dev/tty.usbmodem{serialNumber}";
                                            if (File.Exists(alternativeDevicePath))
                                            {
                                                AnsiConsole.MarkupLine($"[grey]Found {device.chip} device at {alternativeDevicePath}[/]");
                                                esp32Devices.Add(new Esp32Device(alternativeDevicePath, device.s3, device.lilygo, false));
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.MarkupLine($"[red]Error reading macOS device info: {ex.Message}[/]");
                            }
                        }

                    }
                }
            }
        }
    }

    public static async Task<string> GetAvailableDevices()
    {
        esp32Devices.Clear();
        wifiDevice = new Esp32Device("", false, false, false);
        GetPortNames();
        var result = await Esp32Device.CheckZeDMDs(esp32Devices, wifiDevice);
        esp32Devices = result.esp32Devices;
        wifiDevice = result.wifiDevice;
        return result.logs;
    }

}