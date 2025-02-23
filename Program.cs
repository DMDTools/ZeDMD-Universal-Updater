using Spectre.Console;

namespace ZeDMDUpdater;

class Program
{
    private const string SELECTED_MARK = "[X]";
    private const string UNSELECTED_MARK = "[ ]";
    static string selectedBoardType = "Standard";
    static bool useWifi = false;
    static async Task Main(string[] args)
    {
        bool firmwareDownloaded = false;
        string firmwarePath = string.Empty;
        Console.Clear();

        while (true)
        {
            AnsiConsole.Write(new FigletText("ZeDMD Updater")
                .Color(Color.Blue));
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

            string selectBoardTypeChoice;
            if (string.IsNullOrEmpty(selectedBoardType))
            {
                selectBoardTypeChoice = "Select Board Type";
            }
            else
            {
                selectBoardTypeChoice = selectedBoardType == "LilygoS3Amoled"
                    ? $"Select Board Type ({selectedBoardType}, WiFi: {(useWifi ? "Yes" : "No")})"
                    : $"Select Board Type ({selectedBoardType})";
            }
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .AddChoices(new[]
                    {
                selectBoardTypeChoice,
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

                    case var c when c.StartsWith("Select Board Type"):
                        var boardTypes = new[] { "Standard", "LilygoS3Amoled", "S3-N16R8", "← Back" };
                        var boardSelection = new SelectionPrompt<string>()
                            .Title("Select board type:")
                            .AddChoices(boardTypes)
                            .UseConverter(x => x)
                            .HighlightStyle(new Style(foreground: Color.Blue));

                        var boardType = AnsiConsole.Prompt(boardSelection);

                        if (boardType != "← Back")
                        {
                            selectedBoardType = boardType;
                            if (boardType == "LilygoS3Amoled")
                            {
                                useWifi = AnsiConsole.Prompt(
                                    new ConfirmationPrompt("Would you like to use WiFi?")
                                        .ShowChoices());

                                AnsiConsole.MarkupLine($"[green]Board type set to {selectedBoardType} (WiFi: {(useWifi ? "Yes" : "No")})[/]");
                            }
                            else
                            {
                                useWifi = false;
                                AnsiConsole.MarkupLine($"[green]Board type set to {selectedBoardType}[/]");
                            }
                            firmwareDownloaded = false;
                        }
                        Console.Clear();
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
                            // Get available ports
                            var deviceManager = new DeviceManager();
                            var ports = await deviceManager.GetAvailablePorts();

                            if (ports.Count == 0)
                            {
                                AnsiConsole.MarkupLine("[red]No serial ports found[/]");
                                return;
                            }

                            var portChoices = new List<string>(ports);
                            portChoices.Add("← Back");

                            var portSelection = new SelectionPrompt<string>()
                                .Title("Select serial port:")
                                .AddChoices(portChoices)
                                .UseConverter(x => x)
                                .HighlightStyle(new Style(foreground: Color.Blue));

                            var selectedPort = AnsiConsole.Prompt(portSelection);

                            if (selectedPort == "← Back")
                            {
                                Console.Clear();
                                break;
                            }

                            var isS3 = selectedBoardType == "ESP32-S3";

                            if (!File.Exists(firmwarePath + "/ZeDMD.bin"))
                            {
                                AnsiConsole.MarkupLine("[red]Firmware file not found. Please download it first.[/]");
                                return;
                            }
                            await AnsiConsole.Status()
                                .StartAsync("Flashing firmware...", async ctx =>
                                {
                                    ctx.Spinner(Spinner.Known.Dots);
                                    ctx.SpinnerStyle(Style.Parse("orange1"));

                                    bool success = await deviceManager.FlashFirmware(
                                        firmwarePath + "/ZeDMD.bin",
                                        selectedPort,
                                        isS3,
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
                        await AnsiConsole.Status()
                            .StartAsync("Loading device settings...", async ctx =>
                            {
                                ctx.Spinner(Spinner.Known.Dots);
                                ctx.SpinnerStyle(Style.Parse("purple"));

                                AnsiConsole.MarkupLine("[yellow]Function not yet implemented[/]");
                                await Task.Delay(2000);
                                Console.Clear();
                            });
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
}
