using HidApi;
using Microsoft.Extensions.Logging;

public readonly record struct DeviceIdentifier(ushort Vendor, ushort Product) {
    public static DeviceIdentifier Parse(string input) {
        var parts = input.Split(':');
        if (parts.Length != 2) throw new ArgumentException($"Invalid VID:PID format: {input}");

        ushort vendor = Convert.ToUInt16(parts[0], 16);
        ushort product = Convert.ToUInt16(parts[1], 16);

        return new DeviceIdentifier(vendor, product);
    }
}

static class Application {
    public static String Name { get; } = "SteamHaptics";

    public static Random Random { get; } = new Random();
    public static ILogger Logger { get; private set; } = null!;

    public static Entrypoint.Options? Options { get; set; } = null;

    public static void Run(Entrypoint.Options options) {
        Application.Options = options;
        
        Logger = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => {
            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fffffff] ";
            options.SingleLine = true;
        }).SetMinimumLevel(IntoLogLevel(options.LogLevel))).CreateLogger(Application.Name);

        DeviceIdentifier identifier = DeviceIdentifier.Parse(options.Hid);
        Device device = new Device(identifier.Vendor, identifier.Product);
        
        Logger.LogDebug("Launching {0}:", Application.Name);
        Logger.LogDebug("--- Steam Controller HID: {0}", options.Hid);
        Logger.LogDebug("--- Using device: {0}", device.GetProduct());

        SteamHaptics.HapticStreamer.StartStreaming(device);

        SteamHaptics.MonoFrame[] source = SteamHaptics.Utils.Load(options.Filepath);
        long index = 0;

        do {
            var sample = source[index % source.Length];
            var result = SteamHaptics.HapticStreamer.Buffer.TryWrite(sample.AsStereo());

            // TODO(mathias): improve producer waiting logic
            index += result ? 1 : 0; // only advance on successful write
            Thread.Sleep(3);
        } while (options.Repeat || index < source.Length);

        SteamHaptics.HapticStreamer.StopStreaming(device, true);
    }

    private static LogLevel IntoLogLevel(string level) {
        return level.ToLower() switch {
            "debug" => LogLevel.Debug,
            "information" => LogLevel.Information,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => throw new ArgumentException($"Invalid log level: {level}")
        };
    }
}
