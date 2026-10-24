using System.Text.Json;
using WinMaint.Reporting;

try
{
    if (args.Length == 4 && args[0] == "--cleanup-lab")
    {
        var result = CleanupLabEvidenceVerifier.Verify(args[1], args[2], args[3]);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Accepted ? 0 : 5;
    }
    if (args.Length is 3 or 4 && args[0] == "--r0-report")
    {
        var requirements = JsonSerializer.Deserialize<ReadOnlyReportRequirements>(await File.ReadAllTextAsync(args[2]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (requirements is null) return 3;
        string json = await File.ReadAllTextAsync(args[1]);
        var result = ReadOnlyReportAcceptanceVerifier.Verify(json, requirements);
        if (result.Accepted && args.Length == 4)
        {
            string text = await File.ReadAllTextAsync(args[3]);
            if (!ReportRenderer.IsMatchingExport(json, text) || ReportRenderer.ToText(ReportRenderer.Parse(json)) != text)
                result = new(false, ["text-export-mismatch"]);
        }
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Accepted ? 0 : 5;
    }
    Console.Error.WriteLine("Use --cleanup-lab PACKAGE RESULTS EXPECTED_EXE_SHA256 or --r0-report REPORT REQUIREMENTS [TXT].");
    return 3;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Accepted = false, Failure = exception.GetType().Name }));
    return 5;
}
