namespace Conductor.Shared;

public static class Helper
{
    public static void ShowHelp()
    {
        ShowSignature();
        Console.WriteLine(
            $"Usage: {Constants.VersionHeader} \n" +
            "   [Options]: \n" +
            "   -h --help      Show this help message\n" +
            "   -v --version   Show version information\n" +
            "   -e --environment  Use environment variables for configuration\n" +
            "   -f --file  Use .env file for configuration\n"
            );
    }

    public static void ShowVersion()
    {
        Console.WriteLine(Constants.VersionHeader);
    }

    private static void ShowSignature()
    {
        Console.WriteLine(
            "Developed by Leonardo M. Baptista\n"
        );
    }
}