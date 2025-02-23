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
}
