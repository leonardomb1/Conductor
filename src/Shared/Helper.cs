using System.Data;
using System.Net;
using System.Text;
using Serilog;

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

    public static Int64 CalculateBytesUsed(DataTable data)
    {
        Int64 bytes = 0;
        foreach (DataRow row in data.Rows)
        {
            foreach (DataColumn col in data.Columns)
            {
                if (row[col] is null || row[col] == DBNull.Value) continue;
                bytes += GetTypeByteSize(row[col], col.DataType);
            }
        }
        return bytes;
    }

    public static Int64 GetTypeByteSize(object value, Type type)
    {
        return type switch
        {
            _ when type == typeof(string) => Encoding.UTF8.GetByteCount((string)value),
            _ when type == typeof(byte[]) => ((byte[])value).LongLength,
            _ when type == typeof(DateTime) => sizeof(long),
            _ when type == typeof(bool) => sizeof(bool),
            _ when type == typeof(int) => sizeof(int),
            _ when type == typeof(long) => sizeof(long),
            _ when type == typeof(decimal) => sizeof(decimal),
            _ when type == typeof(double) => sizeof(double),
            _ when type == typeof(float) => sizeof(float),
            _ when type == typeof(short) => sizeof(short),
            _ when type == typeof(byte) => sizeof(byte),
            _ => value.ToString()?.Length * sizeof(char) ?? 0
        };
    }
}