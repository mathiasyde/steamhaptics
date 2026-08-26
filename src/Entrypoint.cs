using CommandLine;


class Entrypoint {
    public class Options {
        [Value(0, Required = true)]
        public string Filepath { get; set; } = "";
        [Option("hid", Required = false, HelpText = "HID of the Steam Controller. Wired is 28DE:1302, Wireless is 28DE:1304")]
        public string Hid { get; set; } = "28DE:1302";
        [Option("repeat", Required = false, HelpText = "Repeat indefinitely")]
        public bool Repeat { get; set; } = false;
        [Option("log", Required = false, HelpText = "Log level (Debug, Information, Warning, Error)")]
        public string LogLevel { get; set; } = "Information";
    }

    static void Main(string[] args) {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(Application.Run);
    }
}
