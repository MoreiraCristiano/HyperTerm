using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

string mode = args.FirstOrDefault() ?? "echo";
switch (mode)
{
    case "echo":
        await EchoAsync();
        break;
    case "utf8-split":
        Console.Write(new string('x', 65_535));
        Console.Write("🙂漢字");
        break;
    case "high-output":
        int bytes = args.Length > 1 && int.TryParse(args[1], out int parsed)
            ? parsed
            : 50 * 1024 * 1024;
        await WriteHighVolumeAsync(bytes);
        break;
    case "exit":
        Environment.ExitCode = args.Length > 1 && int.TryParse(args[1], out int exitCode)
            ? exitCode
            : 0;
        break;
    case "hang":
        Console.WriteLine("READY");
        await Task.Delay(Timeout.InfiniteTimeSpan);
        break;
    case "spawn-child":
        using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            ArgumentList = { "hang" },
            UseShellExecute = false,
        }))
        {
            Console.WriteLine("CHILD_STARTED");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }

        break;
    default:
        Console.Error.WriteLine($"Unknown mode: {mode}");
        Environment.ExitCode = 64;
        break;
}

static async Task EchoAsync()
{
    char[] buffer = new char[4096];
    while (true)
    {
        int read = await Console.In.ReadAsync(buffer);
        if (read == 0)
        {
            return;
        }

        await Console.Out.WriteAsync(buffer.AsMemory(0, read));
        await Console.Out.FlushAsync();
    }
}

static async Task WriteHighVolumeAsync(int byteCount)
{
    byte[] chunk = Enumerable.Range(0, 64 * 1024)
        .Select(index => (byte)('A' + (index % 26)))
        .ToArray();
    Stream output = Console.OpenStandardOutput();
    int remaining = byteCount;
    while (remaining > 0)
    {
        int count = Math.Min(chunk.Length, remaining);
        await output.WriteAsync(chunk.AsMemory(0, count));
        remaining -= count;
    }

    await output.FlushAsync();
}
