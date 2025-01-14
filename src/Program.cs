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
            string o when o == "-e" || o == "--environment" => Initializer.InitializeFromEnvVar,
            string o when o == "-f" || o == "--file" => () => Initializer.InitializeFromDotEnv(args.ElementAt(1)),
            null => Helper.ShowHelp,
            _ => () => { Console.WriteLine("This option is invalid."); Helper.ShowHelp(); }
        };

        switcher.Invoke();
    }
}
