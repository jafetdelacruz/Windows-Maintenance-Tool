using System.Globalization;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length == 0) return 3;
switch (args[0])
{
    case "menu":
        using (var input = Console.OpenStandardInput())
            for (var choice = 0; choice < 3; choice++)
            {
                Console.Write("Opción: ");
                Console.Out.Flush();
                var bytes = new List<byte>();
                int next;
                while ((next = input.ReadByte()) != -1 && next != '\n')
                {
                    bytes.Add((byte)next);
                    if (bytes.Count > 128) return 4;
                }
                // Raw bytes consumed by the child, not a reconstruction of planned input.
                Console.WriteLine("CONSUMED:" + Convert.ToHexString(bytes.ToArray()));
            }
        return 0;
    case "eof":
        using (var input = Console.OpenStandardInput())
            Console.WriteLine(input.ReadByte() == -1 ? "EOF RECEIVED" : "UNEXPECTED BYTE");
        return 0;
    case "arguments":
        foreach (var argument in args.Skip(1)) Console.WriteLine("ARG:" + argument);
        Console.Error.WriteLine("STDERR: áéíóú");
        return 0;
    case "nonzero":
        Console.Error.WriteLine("EXPECTED FAILURE");
        return 23;
    case "timeout":
        if (args.Length != 2) return 3;
        await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        await Task.Delay(TimeSpan.FromSeconds(5));
        return 0;
    default: return 3;
}
