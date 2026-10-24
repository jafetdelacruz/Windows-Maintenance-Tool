using Spectre.Console;

namespace WinMaint.Cli;

internal static class ConsoleFactory
{
    public static IAnsiConsole Create(bool plainText)
    {
        var settings = new AnsiConsoleSettings
        {
            Ansi = plainText ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = plainText ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(Console.Out),
        };

        return AnsiConsole.Create(settings);
    }
}
