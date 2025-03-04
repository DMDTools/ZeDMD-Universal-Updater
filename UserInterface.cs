using System.Runtime.InteropServices;
using Spectre.Console;

namespace ZeDMDUpdater;

public static class UserInterface
{
    public static string? CurrentVersion { get; private set; }
    public static async Task ShowVersions()
    {
        // First fetch the data without the status display
        var latestVersion = await InternetFirmwares.GetAvailableVersions();
        var versions = InternetFirmwares.GetVersionsList();

        // If CurrentVersion is null, set it to the latest version
        if (CurrentVersion == null)
        {
            CurrentVersion = versions.Last();
        }
        // Create the table first
        var table = new Table()
            .AddColumn(new TableColumn("Version").Width(15).NoWrap())
            .AddColumn(new TableColumn("Status").Width(15).NoWrap())
            .Expand();

        // Create description panel with limited height
        var descriptionPanel = new Panel(string.Empty)
            .Header("Release Notes")
            .Padding(new Padding(1))
            .BorderStyle(Style.Parse("yellow"));

        // Create a layout with fixed proportions
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Main")
                    .SplitColumns(
                        new Layout("Versions")
                            .Size(28),
                        new Layout("Notes")
                    )
            );

        var choices = versions.ToList();
        choices.Add("q (Quit)");
        var currentChoiceIndex = 0;

        void UpdateDisplay(int selectedIndex)
        {
            Console.Clear();
            var currentVersion = CurrentVersion ?? versions.Last();

            // Update table
            table.Rows.Clear();
            foreach (var version in versions)
            {
                var status = version == $"v{latestVersion}" ? "[green]Latest[/]" :
                            version == CurrentVersion ? "[yellow]Selected[/]" :
                            "[blue]Available[/]";
                // Highlighting for the selected version
                var versionText = version == currentVersion ?
                    $"[yellow]{version}[/]" : version;
                table.AddRow(version, status);
            }

            // Update description
            descriptionPanel = new Panel(
                Markup.Escape(InternetFirmwares.GetReleaseDescription(currentVersion)))
                .Header("Release Notes")
                .Padding(new Padding(1))
                .BorderStyle(Style.Parse("yellow"))
                .Expand();

            // Create left panel with versions list
            var versionTable = new Table()
                .HideHeaders()
                .AddColumn(new TableColumn("Version"))
                .Expand();

            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var row = "";
                if (i == selectedIndex)
                {
                    row = $"[yellow]> {choice}[/]";
                }
                else
                {
                    row = $"  {choice}";
                }

                if (choice == currentVersion)
                {
                    row += " *";
                }

                versionTable.AddRow(row);
            }

            var versionPanel = new Panel(versionTable)
                .Header("Select version")
                .Padding(new Padding(1))
                .BorderStyle(Style.Parse("green"))
                .Expand();

            // Update layout
            layout["Versions"].Update(versionPanel);
            layout["Notes"].Update(descriptionPanel);

            // Write the layout
            AnsiConsole.Write(layout);
        }

        // Show initial display
        currentChoiceIndex = versions.Count - 1;
        UpdateDisplay(currentChoiceIndex);

        while (true)
        {
            try
            {
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        currentChoiceIndex = (currentChoiceIndex - 1 + choices.Count) % choices.Count;
                        UpdateDisplay(currentChoiceIndex);
                        break;

                    case ConsoleKey.DownArrow:
                        currentChoiceIndex = (currentChoiceIndex + 1) % choices.Count;
                        UpdateDisplay(currentChoiceIndex);
                        break;

                    case ConsoleKey.Escape:
                        Console.Clear();
                        return;
                    case ConsoleKey.Enter:
                        var selection = choices[currentChoiceIndex];
                        if (selection == "q (Quit)")
                        {
                            Console.Clear();
                            return;
                        }
                        CurrentVersion = selection;
                        UpdateDisplay(currentChoiceIndex);
                        break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]An error occurred: {ex.Message}[/]");
                return;
            }
        }
    }

    internal static void ShowBrightness()
    {
        ShowTop();
        // Set device brightness
        nint _pZeDMD;
        bool dmdDevice = OpenZeDMD(out _pZeDMD);
        if (!dmdDevice)
        {
            return;
        }

        nint pImageRgb = ShowCalibrationImage(_pZeDMD);


        // Get the current brightness
        var currentBrightness = Program.selectedDevice.Brightness;

        // Use a Spectre Console bar to set the brightness
        int currentValue = currentBrightness;
        bool selecting = true;

        while (selecting)
        {
            ShowTop();
            pImageRgb = ShowCalibrationImage(_pZeDMD);
            var progress = new Progress(AnsiConsole.Console);
            progress.Start(ctx =>
            {
                var task = ctx.AddTask($"[green]Brightness ({currentValue}/15)[/]", maxValue: 15);
                task.Value = currentValue;
                task.IsIndeterminate = false;
            });

            AnsiConsole.MarkupLine($"\nUse [blue]:left_arrow:[/] and [blue]:right_arrow:[/] to adjust, [green]Enter[/] to confirm");
            AnsiConsole.MarkupLine($"Current value: {currentValue}");

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (currentValue > 0)
                    {
                        currentValue--;
                        Esp32Device.ZeDMD_SetBrightness(_pZeDMD, (byte)currentValue);
                    }
                    break;
                case ConsoleKey.RightArrow:
                    if (currentValue < 15)
                    {
                        currentValue++;
                        Esp32Device.ZeDMD_SetBrightness(_pZeDMD, (byte)currentValue);
                    }
                    break;
                case ConsoleKey.Enter:
                    selecting = false;
                    break;
            }
        }
        AnsiConsole.MarkupLine($"[green]Setting brightness to {currentValue}...[/]");
        Esp32Device.ZeDMD_SetBrightness(_pZeDMD, (byte)currentValue);
        Program.selectedDevice.Brightness = currentValue;

        // Save settings before rendering
        Esp32Device.ZeDMD_SaveSettings(_pZeDMD);

        currentBrightness = currentValue;
        if (pImageRgb != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(pImageRgb);
        }

        Esp32Device.ZeDMD_Close(_pZeDMD);

    }

    private static bool OpenZeDMD(out nint _pZeDMD)
    {
        // create an instance
        GCHandle handle;
        _pZeDMD = IntPtr.Zero;
        _pZeDMD = Esp32Device.ZeDMD_GetInstance();

        Esp32Device.ZeDMD_LogCallback callbackDelegate = new Esp32Device.ZeDMD_LogCallback((format, args, userData) =>
        {
            var message = Marshal.PtrToStringAnsi(Esp32Device.ZeDMD_FormatLogMessage(format, args, userData));
            AnsiConsole.MarkupLine($"[grey]{message}[/]");
        });

        // Keep a reference to the delegate to prevent GC from collecting it
        handle = GCHandle.Alloc(callbackDelegate);
        Esp32Device.ZeDMD_SetLogCallback(_pZeDMD, callbackDelegate, IntPtr.Zero);
        Esp32Device.ZeDMD_SetDevice(_pZeDMD, Program.selectedDevice.DeviceAddress);
        // Esp32Device.ZeDMD_Reset(_pZeDMD);
        var openOK = Esp32Device.ZeDMD_Open(_pZeDMD);
        if (!openOK)
        {
            AnsiConsole.MarkupLine($"[red]Failed to open device {Program.selectedDevice.DeviceAddress}[/]");
            Thread.Sleep(2000);
            return false;
        }

        return true;
    }

    internal static void ShowTop()
    {
        Console.Clear();
        AnsiConsole.Write(new FigletText("ZeDMD Updater")
            .Color(Color.Blue));
        var panel = new Panel(
            $"[blue]Device:[/] {(Program.selectedDevice.isZeDMD ? Program.selectedDevice.WifiIp : Program.selectedDevice.DeviceAddress)} | " +
            $"[blue]Board:[/] {(Program.selectedDevice.isLilygo ? "LilygoS3Amoled" : (Program.selectedDevice.isS3 ? "ESP32-S3" : "Standard"))} | " +
            $"[blue]ID:[/] 0x{Program.selectedDevice.ZeID:X4} | " +
            $"[blue]Version:[/] {Program.selectedDevice.MajVersion}.{Program.selectedDevice.MinVersion}.{Program.selectedDevice.PatVersion}")
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1),
        };
        AnsiConsole.Write(panel);
    }

    private static nint ShowCalibrationImage(nint _pZeDMD)
    {
        // Display the calibration image
        Esp32Device.ZeDMD_SetFrameSize(_pZeDMD, 128, 32);
        IntPtr pImageRgb = IntPtr.Zero;
        var imageRgbArray = Esp32Device.ReadRawRGBFile("RGBW.raw");

        pImageRgb = Marshal.AllocHGlobal(imageRgbArray.Length);
        Marshal.Copy(imageRgbArray, 0, pImageRgb, imageRgbArray.Length);
        // Render and keep displayed
        Esp32Device.ZeDMD_ClearScreen(_pZeDMD);
        Esp32Device.ZeDMD_RenderRgb888(_pZeDMD, pImageRgb);
        return pImageRgb;
    }

    internal static void ShowRgbOrder()
    {
        // Set RGB Order
        ShowTop();
        nint _pZeDMD;
        bool dmdDevice = OpenZeDMD(out _pZeDMD);
        if (!dmdDevice)
        {
            return;
        }

        nint pImageRgb = ShowCalibrationImage(_pZeDMD);

        // Get the current RGB Order
        var currentRGBOrder = Program.selectedDevice.RgbOrder;

        // Use a Spectre Console bar to set the RGB Order
        int currentValue = currentRGBOrder;
        bool selecting = true;

        while (selecting)
        {
            ShowTop();
            pImageRgb = ShowCalibrationImage(_pZeDMD);
            var progress = new Progress(AnsiConsole.Console);
            progress.Start(ctx =>
            {
                var task = ctx.AddTask($"[green]RGB Order ({currentValue}/5)[/]", maxValue: 5);
                task.Value = currentValue;
                task.IsIndeterminate = false;
            });

            AnsiConsole.MarkupLine($"\nUse [blue]←[/] and [blue]→[/] to adjust, [green]Enter[/] to confirm");
            AnsiConsole.MarkupLine($"Current value: {currentValue}");

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (currentValue > -0)
                    {
                        currentValue--;
                        AnsiConsole.MarkupLine($"[green]Setting RGB Order to {currentValue}...[/]");
                        Esp32Device.ZeDMD_SetRGBOrder(_pZeDMD, (byte)currentValue);
                        Esp32Device.ZeDMD_SaveSettings(_pZeDMD);
                        Esp32Device.ZeDMD_Reset(_pZeDMD);
                    }
                    break;
                case ConsoleKey.RightArrow:
                    if (currentValue < 5)
                    {
                        currentValue++;
                        AnsiConsole.MarkupLine($"[green]Setting RGB Order to {currentValue}...[/]");
                        Esp32Device.ZeDMD_SetRGBOrder(_pZeDMD, (byte)currentValue);
                        Esp32Device.ZeDMD_SaveSettings(_pZeDMD);
                        Esp32Device.ZeDMD_Reset(_pZeDMD);
                    }
                    break;
                case ConsoleKey.Enter:
                    selecting = false;
                    break;
            }
        }
        Program.selectedDevice.RgbOrder = currentValue;

        if (pImageRgb != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(pImageRgb);
        }

        AnsiConsole.MarkupLine($"[green]Closing device...[/]");
        Esp32Device.ZeDMD_Close(_pZeDMD);
    }

    internal static void ShowTransportMode()
    {
        throw new NotImplementedException();
    }

    internal static void ShowUdpDelay()
    {
        throw new NotImplementedException();
    }

    internal static void ShowUsbPacketSize()
    {
        throw new NotImplementedException();
    }

    internal static void ShowWifiSettings()
    {
        ShowTop();
        nint _pZeDMD;
        bool dmdDevice = OpenZeDMD(out _pZeDMD);
        if (!dmdDevice)
        {
            return;
        }
    
        try
        {
            // Get current WiFi settings
            var currentSSID = Program.selectedDevice.SSID;
            var currentPort = Program.selectedDevice.SSIDPort;
    
            var ssid = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter WiFi SSID:")
                    .DefaultValue(currentSSID)
                    .AllowEmpty());
    
            var password = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter WiFi Password:")
                    .Secret()
                    .AllowEmpty());
    
            var port = AnsiConsole.Prompt(
                new TextPrompt<int>("Enter WiFi Port:")
                    .DefaultValue(currentPort)
                    .ValidationErrorMessage("[red]Please enter a valid port number (1-65535)[/]")
                    .Validate(port =>
                    {
                        return port switch
                        {
                            < 1 => ValidationResult.Error("[red]Port must be greater than 0[/]"),
                            > 65535 => ValidationResult.Error("[red]Port must be less than 65536[/]"),
                            _ => ValidationResult.Success(),
                        };
                    }));
    
            // Confirm settings
            var table = new Table()
                .AddColumn("Setting")
                .AddColumn("Value");
    
            table.AddRow("SSID", ssid);
            table.AddRow("Password", "********");
            table.AddRow("Port", port.ToString());
    
            AnsiConsole.Write(table);
    
            if (AnsiConsole.Confirm("Apply these settings?"))
            {
                AnsiConsole.Status()
                    .Start("Applying WiFi settings...", async ctx =>
                    {
                        // Apply the settings
                        Esp32Device.ZeDMD_SetWiFiSSID(_pZeDMD, ssid);
                        if (!string.IsNullOrEmpty(password))
                        {
                            Esp32Device.ZeDMD_SetWiFiPassword(_pZeDMD, password);
                        }
                        Esp32Device.ZeDMD_SetWiFiPort(_pZeDMD, port);
                        // Change transport mode
                        Esp32Device.ZeDMD_SetTransport(_pZeDMD, 1);
    
                        // Save settings
                        Esp32Device.ZeDMD_SaveSettings(_pZeDMD);
                        Thread.Sleep(2000);
                        Esp32Device.ZeDMD_Reset(_pZeDMD);
  
                        AnsiConsole.MarkupLine("[green]WiFi settings updated successfully![/]");
                        Thread.Sleep(2000); // Give user time to read the message
                        await Program.GetZeDMDDevices();
                    });
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error updating WiFi settings: {ex.Message}[/]");
            Thread.Sleep(2000);
        }
        finally
        {
            // Always close the device
            Esp32Device.ZeDMD_Close(_pZeDMD);
        }
    }
    
}
