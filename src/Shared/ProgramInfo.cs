namespace Conductor.Shared;

public static class ProgramInfo
{
    public const string ProgramVersion = "0.0.2";

    public const string ProgramName = "Conductor";

    public static string VersionHeader => $"{ProgramName} - Version: {ProgramVersion}";

    public static void ShowHelp()
    {
        ShowSignature();
        Console.WriteLine(
            $"Usage: {VersionHeader} \n" +
            "   [Options]: \n" +
            "   -h --help      Show this help message\n" +
            "   -v --version   Show version information\n" +
            "   -e --environment  Use environment variables for configuration\n" +
            "   -f --file  Use .env file for configuration\n"
            );
    }

    public static void ShowVersion()
    {
        Console.WriteLine(VersionHeader);
    }

    private static void ShowSignature()
    {
        Console.WriteLine(
            "Developed by Leonardo M. Baptista\n"
        );
    }
}
