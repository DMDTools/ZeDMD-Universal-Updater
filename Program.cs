// TODO :
// - Look for ZeDMD device at startup, and use that for flashing
// - Add a "Device Settings" option to the main menu, and ways to set the following :
//   - Transport mode (WiFi, USB)
//   - WiFi SSID and password
//   - RGB order
//   - Brightness
//   - USB packet size
//   - UDP Delay
// - FIX 
//   - If there is more than one device, we need to ask the user to select the device at the beginning and use this throughout
using Spectre.Console;

namespace ZeDMDUpdater;

class Program
{
    private const string SELECTED_MARK = "[X]";
    private const string UNSELECTED_MARK = "[ ]";
    static string selectedBoardType = "Standard";
    static bool useWifi = false;
    public static Esp32Device selectedDevice = new Esp32Device("", false, false, false);
    internal static async Task GetZeDMDDevices()
    {
        // Get available devices
        string deviceLogs = await Esp32Devices.GetAvailableDevices();
        ShowDeviceSummary();
        // If there is more that one device in Esp32Devices.esp32Devices, we need to ask the user to select the device
        if (Esp32Devices.esp32Devices.Count > 1)
        {
            var deviceChoices = new List<string>();
            foreach (var device in Esp32Devices.esp32Devices)
            {
                deviceChoices.Add($"{device.DeviceAddress} - {device.ZeID:X4}, version {device.MajVersion}.{device.MinVersion}.{device.PatVersion}");
            }
            deviceChoices.Add("< Back");

            var deviceSelection = new SelectionPrompt<string>()
                .Title("Select device:")
                .AddChoices(deviceChoices)
                .UseConverter(x => x)
                .HighlightStyle(new Style(foreground: Color.Blue));

            var selectedDeviceInMenu = AnsiConsole.Prompt(deviceSelection);

            if (selectedDeviceInMenu == "< Back")
            {
                Console.Clear();
            }

            // Get the actual device from the list
            selectedDevice = Esp32Devices.esp32Devices.Find(x => x.DeviceAddress == selectedDeviceInMenu.Split(" - ")[0])
                          ?? throw new InvalidOperationException("Selected device not found.");
        }
        else
        {
            if (Esp32Devices.wifiDevice.isWifi && Esp32Devices.wifiDevice.isZeDMD)
            {
                selectedDevice = Esp32Devices.wifiDevice;
            }
            else
            {
                selectedDevice = Esp32Devices.esp32Devices[0];
            }
        }
        // Ask to press a key with Spectre.Console
        AnsiConsole.MarkupLine("Press any key to continue...");
        Console.ReadKey();

    }
    static async Task Main(string[] args)
    {
        bool firmwareDownloaded = false;
        string firmwarePath = string.Empty;
        try
        {
            AnsiConsole.MarkupLine("[yellow]=== Initializing and scanning for ZeDMD devices ===[/]");
            await GetZeDMDDevices();

        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during initialization: {ex.Message}[/]");
            return;
        }
        Console.Clear();

        while (true)
        {
            UserInterface.ShowTop();

            string selectVersionChoice = "Select Version";
            string downloadFirmwareChoice = "Download Firmware";

            // Version selection status
            if (string.IsNullOrEmpty(UserInterface.CurrentVersion))
            {
                selectVersionChoice = $"{Markup.Escape(UNSELECTED_MARK)} Select Version"; // Empty checkbox at start
            }
            else
            {
                selectVersionChoice = $"{Markup.Escape(SELECTED_MARK)} Select Version ({UserInterface.CurrentVersion})"; // Green checkbox at start
            }

            // Download firmware status
            if (firmwareDownloaded)
            {
                downloadFirmwareChoice = $"{Markup.Escape(SELECTED_MARK)} Download Firmware"; // Green checkbox if firmware exists
            }
            else
            {
                downloadFirmwareChoice = $"{Markup.Escape(UNSELECTED_MARK)} Download Firmware"; // Empty checkbox if no firmware
            }

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .AddChoices(new[]
                    {
                selectVersionChoice,
                downloadFirmwareChoice,
                "Flash Device",
                "Device Settings",
                "Exit"
                    }));

            try
            {
                switch (choice)
                {
                    case var c when c.StartsWith($"{Markup.Escape(UNSELECTED_MARK)} Select Version") ||
                                    c.StartsWith($"{Markup.Escape(SELECTED_MARK)} Select Version"):
                        await UserInterface.ShowVersions();
                        firmwareDownloaded = false;
                        break;

                    case var c when c.StartsWith($"{Markup.Escape(UNSELECTED_MARK)} Download Firmware") ||
                                    c.StartsWith($"{Markup.Escape(SELECTED_MARK)} Download Firmware"):
                        // If UserInterface.CurrentVersion is null or empty, prompt the user to select a version
                        if (string.IsNullOrEmpty(UserInterface.CurrentVersion))
                        {
                            AnsiConsole.MarkupLine("[red]Please select a version before downloading firmware.[/]");
                            await Task.Delay(2000);
                            Console.Clear();
                            break;
                        }
                        else
                        {
                            await AnsiConsole.Status()
                                    .StartAsync("Downloading firmware...", async ctx =>
                                    {
                                        ctx.Spinner(Spinner.Known.Dots);
                                        ctx.SpinnerStyle(Style.Parse("blue"));

                                        try
                                        {
                                            string extractPath = await InternetFirmwares.DownloadFirmware(
                                                UserInterface.CurrentVersion,
                                                "128x32",
                                                selectedBoardType,
                                                useWifi);
                                            AnsiConsole.MarkupLine($"\n[green]Successfully downloaded and extracted firmware to:[/]");
                                            AnsiConsole.MarkupLine($"[blue]{extractPath}[/]");
                                            firmwareDownloaded = true;
                                            firmwarePath = extractPath;
                                            await Task.Delay(2000);
                                            Console.Clear();
                                        }
                                        catch (Exception ex)
                                        {
                                            AnsiConsole.MarkupLine($"\n[red]{ex.Message}[/]");
                                        }
                                    });
                        }
                        break;

                    case "Flash Device":
                        if (!firmwareDownloaded)
                        {
                            AnsiConsole.MarkupLine("[red]Please download firmware before flashing.[/]");
                            await Task.Delay(2000); // Placeholder delay
                            Console.Clear();
                            break;
                        }
                        try
                        {
                            var deviceManager = new DeviceManager();

                            if (!File.Exists(firmwarePath + "/ZeDMD.bin"))
                            {
                                AnsiConsole.MarkupLine("[red]Firmware file not found. Please download it first.[/]");
                                return;
                            }
                            IntPtr _pZeDMD = IntPtr.Zero;
                            _pZeDMD = Esp32Device.ZeDMD_GetInstance();


                            await AnsiConsole.Status()
                                .StartAsync("Flashing firmware...", async ctx =>
                                {
                                    ctx.Spinner(Spinner.Known.Dots);
                                    ctx.SpinnerStyle(Style.Parse("orange1"));
                                    // FIXME : if there is more than one device, we need to ask the user to select the device
                                    bool success = await deviceManager.FlashFirmware(
                                        firmwarePath + "/ZeDMD.bin",
                                        selectedDevice.DeviceAddress,
                                        selectedDevice.isS3,
                                        (message) => AnsiConsole.MarkupLine($"[blue]{message}[/]"));

                                    if (success)
                                    {
                                        AnsiConsole.MarkupLine("[green]Successfully flashed firmware![/]");
                                    }
                                    else
                                    {
                                        AnsiConsole.MarkupLine("[red]Failed to flash firmware[/]");
                                    }
                                });
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                        }
                        break;

                    case "Device Settings":

                        var deviceSettingsChoice = AnsiConsole.Prompt(
                            new SelectionPrompt<string>()
                                .Title("Select a setting to change:")
                                .AddChoices(new[]
                                {
                                    "Transport Mode",
                                    "WiFi SSID and Password",
                                    "RGB Order",
                                    "Brightness",
                                    "USB Packet Size",
                                    "UDP Delay",
                                    "< Back"
                                }));

                        switch (deviceSettingsChoice)
                        {
                            case "Transport Mode":
                                UserInterface.ShowTransportMode();
                                break;

                            case "WiFi SSID and Password":
                                UserInterface.ShowWifiSettings();
                                break;

                            case "RGB Order":
                                UserInterface.ShowRgbOrder();
                                break;

                            case "Brightness":
                                UserInterface.ShowBrightness();
                                break;

                            case "USB Packet Size":
                                UserInterface.ShowUsbPacketSize();
                                break;

                            case "UDP Delay":
                                UserInterface.ShowUdpDelay();
                                break;

                            case "< Back":
                                Console.Clear();
                                break;
                        }
                        break;


                    case "Exit":
                        AnsiConsole.MarkupLine("[green]Goodbye![/]");
                        return;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                AnsiConsole.WriteLine();
            }
        }
    }

    private static void ShowDeviceSummary()
    {
        // Create a table for the device summary
        var table = new Table()
.Border(TableBorder.Rounded)
.BorderColor(Color.Grey)
.Title("[yellow]Device Summary[/]")
.AddColumn(new TableColumn("[blue]Type[/]").Centered())
.AddColumn(new TableColumn("[blue]Board[/]").Centered())
.AddColumn(new TableColumn("[blue]Location[/]").Centered())
.AddColumn(new TableColumn("[blue]ID[/]").Centered())
.AddColumn(new TableColumn("[blue]Firmware[/]").Centered());

        // Add WiFi device if found
        if (Esp32Devices.wifiDevice.isZeDMD)
        {
            table.AddRow(
"[green]WiFi[/]",
$"[white]{(Esp32Devices.wifiDevice.isLilygo ? "LilygoS3Amoled" : (Esp32Devices.wifiDevice.isS3 ? "ESP32-S3" : "Standard"))}[/]",
$"[white]{Esp32Devices.wifiDevice.WifiIp}[/]",
$"[white]0x{Esp32Devices.wifiDevice.ZeID:X4}[/]",
$"[white]{Esp32Devices.wifiDevice.MajVersion}.{Esp32Devices.wifiDevice.MinVersion}.{Esp32Devices.wifiDevice.PatVersion}[/]"
);
        }

        // Add USB devices
        foreach (var device in Esp32Devices.esp32Devices.Where(d => d.isZeDMD))
        {
            table.AddRow(
"[green]USB[/]",
$"[white]{(Esp32Devices.wifiDevice.isLilygo ? "LilygoS3Amoled" : (Esp32Devices.wifiDevice.isS3 ? "ESP32-S3" : "Standard"))}[/]",
$"[white]{device.DeviceAddress}[/]",
$"[white]0x{device.ZeID:X4}[/]",
$"[white]{device.MajVersion}.{device.MinVersion}.{device.PatVersion}[/]"
);
        }

        // If no devices found, add a message row
        if (!Esp32Devices.wifiDevice.isZeDMD && !Esp32Devices.esp32Devices.Any(d => d.isZeDMD))
        {
            table.AddRow(
"[red]No devices found[/]",
"",
"",
""
);
        }

        // Display the table
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

    }
}
