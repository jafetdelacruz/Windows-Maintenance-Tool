using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class DiagnosticParserTests
{
    [Theory]
    [InlineData(19045, "Windows 10")]
    [InlineData(19046, "Windows 10")]
    [InlineData(22000, "Windows 11")]
    [InlineData(26200, "Windows 11")]
    public void WindowsBuildsAreClassifiedWithoutUsingLegacyProductName(int build, string expected)
    {
        Assert.Equal(expected, WindowsVersionClassifier.Classify(build));
    }

    [Fact]
    public void Windows10MemoryFixturePreservesMissingManufacturerAndSpeed()
    {
        var modules = CimJsonParser.ParseMemoryModules(ReadFixture("memory-windows10.json"));

        Assert.Equal(2, modules.Count);
        Assert.Null(modules[1].Manufacturer);
        Assert.Null(modules[1].ConfiguredClockSpeed);
    }

    [Theory]
    [InlineData("storage-windows10.json", "SATA", null)]
    [InlineData("storage-windows11.json", "NVMe", 37)]
    public void StorageFixturesPreserveSensorCoverage(string fixture, string busType, int? temperature)
    {
        var disk = Assert.Single(CimJsonParser.ParsePhysicalDisks(ReadFixture(fixture)));

        Assert.Equal(busType, disk.BusType);
        Assert.Equal(temperature, disk.Temperature);
    }

    [Fact]
    public void EventFixtureParsesWheaWithoutMachineOrUserIdentifiers()
    {
        var xml = ReadFixture("events-windows10.xml");

        var events = WindowsEventXmlParser.Parse(xml);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, item => item.Provider == "Microsoft-Windows-WHEA-Logger" && item.EventId == 17);
        Assert.DoesNotContain(Environment.MachineName, xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, xml, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadFixture(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [InlineData(0, 4, WheaEventKind.Error)]
    [InlineData(1, 2, WheaEventKind.Error)]
    [InlineData(2, 3, WheaEventKind.Error)]
    [InlineData(3, 4, WheaEventKind.Informational)]
    [InlineData(3, 2, WheaEventKind.Error)]
    [InlineData(99, 4, WheaEventKind.Unknown)]
    public void WheaCperSeverityIsClassifiedConservatively(int severity, int level, object expected)
    {
        var bytes = new byte[128];
        "CPER"u8.CopyTo(bytes);
        Array.Fill(bytes, (byte)255, 6, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), (uint)severity);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 128);
        var record = Assert.Single(WindowsEventXmlParser.Parse(WheaXml(level, Convert.ToHexString(bytes))));
        Assert.Equal((WheaEventKind)expected, record.WheaKind);
    }

    [Theory]
    [InlineData(4, "", WheaEventKind.Unknown)]
    [InlineData(4, "ZZ", WheaEventKind.Unknown)]
    [InlineData(4, "43504552", WheaEventKind.Unknown)]
    [InlineData(3, "", WheaEventKind.Error)]
    public void MissingOrMalformedCperIsNotAssumedHealthy(int level, string raw, object expected)
    {
        Assert.Equal((WheaEventKind)expected, Assert.Single(WindowsEventXmlParser.Parse(WheaXml(level, raw))).WheaKind);
    }

    private static string WheaXml(int level, string raw) => $"""
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
          <System><Provider Name="Microsoft-Windows-WHEA-Logger"/><EventID>3</EventID><Level>{level}</Level></System>
          <EventData><Data Name="RawData">{raw}</Data></EventData>
        </Event>
        """;
}
