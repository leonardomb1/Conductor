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
            string o when o == "-M" || o == "--migrate" => () => Initializer.Migrate(args.ElementAtOrDefault(1)),
            string o when o == "-eM" || o == "--migrate-init-env" => Initializer.MigrateAndInitialize,
            string o when o == "-fM" || o == "--migrate-init-file" => () => Initializer.MigrateAndInitialize(args.ElementAtOrDefault(1)),
            null => Helper.ShowHelp,
            _ => () => { Console.WriteLine("This option is invalid."); Helper.ShowHelp(); }
        };

        switcher.Invoke();
    }
}
