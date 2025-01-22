using Conductor.Shared;

namespace Conductor;

public class Program
{
    public static void Main(string[] args)
    {
        Action switcher = args.FirstOrDefault() switch
        {
            string o when o == "-h" || o == "--help" => Helper.ShowHelp,
            string o when o == "-v" || o == "--version" => Helper.ShowVersion,
            string o when o == "-e" || o == "--environment" => Initializer.StartWithEnvVar,
            string o when o == "-f" || o == "--file" => () => Initializer.StartWithDotEnv(args.ElementAtOrDefault(1)),
            string o when o == "-m" || o == "--migrate" => () => Initializer.Migrate(args.ElementAtOrDefault(1)),
            string o when o == "-x" || o == "--migrate-init" => () => Initializer.MigrateAndInitialize(),
            null => Helper.ShowHelp,
            _ => () => { Console.WriteLine("This option is invalid."); Helper.ShowHelp(); }
        };

        switcher.Invoke();
    }
}
