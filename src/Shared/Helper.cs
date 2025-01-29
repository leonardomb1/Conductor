using System.Net;
using Conductor.Logging;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Connections;

namespace Conductor.Shared;

public static class Helper
{
    public static void ShowHelp()
    {
        ShowSignature();
        Console.WriteLine(
            $"Usage: {ProgramInfo.VersionHeader} \n" +
            "   [Options]: \n" +
            "   -h --help      Show this help message\n" +
            "   -v --version   Show version information\n" +
            "   -e --environment  Use environment variables for configuration\n" +
            "   -f --file  Use .env file for configuration\n" +
            "   -M --migrate Runs a migration in the configured .env database\n" +
            "   -eM --migrate-init-env  Runs a migration before running the server, uses the environment variables for configuration\n" +
            "   -fM --migrate-init-file  Runs a migration before running the server, uses the .env file for configuration\n"
            );
    }

    public static void ShowVersion()
    {
        Console.WriteLine(ProgramInfo.VersionHeader);
    }

    private static void ShowSignature()
    {
        Console.WriteLine(
            "Developed by Leonardo M. Baptista\n"
        );
    }

    public static bool VerifyIpAddress(IPAddress address, HttpContext ctx)
    {
        byte validations = 0;
        for (byte i = 0; i < Settings.AllowedIpsRange.Value.Length; i++)
        {
            if (!Settings.AllowedIpsRange.Value[i].Contains(address)) validations++;
        }

        if (validations == Settings.AllowedIpsRange.Value.Length)
        {
            Log.Out(
                $"Blocking IP Address {address} from connecting to the server using HTTP level blockage.",
                RecordType.Warning,
                callerMethod: "Kestrel"
            );
            return false;
        }

        return true;
    }

    public static void FilterIpAddress(IPAddress address, ConnectionContext ctx)
    {
        byte validations = 0;
        for (byte i = 0; i < Settings.AllowedIpsRange.Value.Length; i++)
        {
            if (!Settings.AllowedIpsRange.Value[i].Contains(address)) validations++;
        }

        if (validations == Settings.AllowedIpsRange.Value.Length)
        {
            Log.Out(
                $"Blocking IP Address {address} from connecting to the server using socket layer blockage.",
                RecordType.Warning,
                callerMethod: "Kestrel"
            );
            ctx.Abort();
            return;
        }
    }
}