using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class PreviousMemoryDiagnosticTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    [Theory]
    [InlineData(1101, 4, "passed")]
    [InlineData(1201, 4, "passed")]
    [InlineData(1102, 2, "failed")]
    [InlineData(1202, 2, "failed")]
    public void KnownResultRequiresCorrectProviderLevelAndDate(int id, int level, string expected)
    {
        var datum = PreviousMemoryDiagnosticProvider.Parse(Event(id, level), Now);
        Assert.Equal(expected, datum.TextValue);
        Assert.Equal(EvidenceCoverage.Partial, datum.Coverage);
        Assert.Contains("resultado histórico", datum.Detail);
        Assert.DoesNotContain("PrivateMachine", datum.Detail);
        Assert.DoesNotContain("PrivateUser", datum.Detail);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("provider")]
    [InlineData("date")]
    [InlineData("future")]
    [InlineData("level")]
    [InlineData("unknown-id")]
    [InlineData("cancelled")]
    [InlineData("incomplete")]
    [InlineData("duplicate")]
    [InlineData("contradictory")]
    [InlineData("bad-page-format")]
    [InlineData("dtd")]
    public void MissingOrAmbiguousEventCannotBecomeHealthy(string reason)
    {
        var xml = Event(1201, 4);
        xml = reason switch
        {
            "empty" => "",
            "invalid" => "not xml",
            "provider" => xml.Replace("MemoryDiagnostics-Results", "Other-Results", StringComparison.Ordinal),
            "date" => xml.Replace(Now.AddDays(-1).ToString("O"), "invalid", StringComparison.Ordinal),
            "future" => xml.Replace(Now.AddDays(-1).ToString("O"), Now.AddDays(1).ToString("O"), StringComparison.Ordinal),
            "level" => Event(1201, 2),
            "unknown-id" => Event(9999, 4),
            "cancelled" => Event(1103, 4),
            "incomplete" => Event(1104, 4),
            "duplicate" => "<Events>" + xml + xml + "</Events>",
            "contradictory" => xml.Replace("<NumBadPages>0", "<NumBadPages>1", StringComparison.Ordinal),
            "bad-page-format" => xml.Replace("<NumBadPages>0", "<NumBadPages>NaN", StringComparison.Ordinal),
            "dtd" => "<!DOCTYPE Event [<!ENTITY x 'test'>]>" + xml,
            _ => xml,
        };
        var datum = PreviousMemoryDiagnosticProvider.Parse(xml, Now);
        Assert.Equal(EvidenceCoverage.Absent, datum.Coverage);
        Assert.Null(datum.TextValue);
    }

    private static string Event(int id, int level) => $"""
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
          <System><Provider Name="Microsoft-Windows-MemoryDiagnostics-Results"/>
          <EventID>{id}</EventID><Level>{level}</Level>
          <TimeCreated SystemTime="{Now.AddDays(-1):O}"/>
          <Computer>PrivateMachine</Computer><Security UserID="PrivateUser"/></System>
          <UserData><Results><NumBadPages>0</NumBadPages></Results></UserData>
        </Event>
        """;
}
