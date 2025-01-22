using System.Reflection;
using System.Threading.Tasks;
using Conductor.Data;
using Conductor.Logging;
using Conductor.Router;
using Conductor.Shared.Config;
using LinqToDB.Data;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Shared;

public static class Initializer
{
    private static void InitializeFromFile(string? envFilePath)
    {
        try
        {
            if (!File.Exists(envFilePath))
            {
                throw new FileNotFoundException($"The .env file was not found at path: {envFilePath}");
            }

            var envVariables = File.ReadAllLines(envFilePath)
                .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.Trim().StartsWith('#'))
                .Select(line => line.Split('=', 2))
                .ToDictionary(parts => parts[0].Trim(), parts => parts.Length > 1 ? parts[1].Trim() : "");

            var properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(prop => prop.GetCustomAttribute<ConfigKeyAttribute>() != null);

            foreach (var property in properties)
            {
                var attribute = property.GetCustomAttribute<ConfigKeyAttribute>();
                if (attribute != null && envVariables.TryGetValue(attribute.Key, out var value))
                {
                    if (property.CanWrite)
                    {
                        value = value.Trim('"');
                        value = value.Replace("\\\"", "\"");
                        var convertedValue = Convert.ChangeType(value, property.PropertyType);
                        property.SetValue(null, convertedValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Property {property.Name} is read-only.");
                    }
                }
                else
                {
                    throw new Exception($"Environment variable '{attribute?.Key}' is missing or not set.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }


    private static void InitializeFromEnvVar()
    {
        try
        {
            var properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(prop => prop.GetCustomAttribute<ConfigKeyAttribute>() != null);

            foreach (var property in properties)
            {
                var attribute = property.GetCustomAttribute<ConfigKeyAttribute>();
                if (attribute != null)
                {
                    var value = Environment.GetEnvironmentVariable(attribute.Key);

                    if (!string.IsNullOrEmpty(value))
                    {
                        if (property.CanWrite)
                        {
                            value = value.Trim('"');
                            value = value.Replace("\\\"", "\"");
                            var convertedValue = Convert.ChangeType(value, property.PropertyType);
                            property.SetValue(null, convertedValue);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Property {property.Name} is read-only.");
                        }
                    }
                    else
                    {
                        throw new Exception($"Environment variable '{attribute.Key}' is missing or not set.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    private static void ServerStartup()
    {
        try
        {
            if (Settings.DevelopmentMode)
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            }

            // Linq2db config
            DataConnection.DefaultSettings = new ConnectionSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }

        Server server = new();
        server.Run();
    }

    private static async Task Migration(Action<string> say)
    {
        using var db = new EfContext();
        if (db.Database.GetPendingMigrations().Any())
        {
            await db.Database.MigrateAsync();
            say($"Database migration complete : {db.Database.GetAppliedMigrations().Last()}");
        }
    }

    public static void StartWithDotEnv(string? envFilePath)
    {
        if (envFilePath == null)
        {
            Console.WriteLine("No .env file path was provided.");
            Environment.Exit(1);
        }
        InitializeFromFile(envFilePath);
        ServerStartup();
    }

    public static void StartWithEnvVar()
    {
        InitializeFromEnvVar();
        ServerStartup();
    }

    public static void Migrate(string? envFilePath)
    {
        if (envFilePath == null)
        {
            Console.WriteLine("No .env file path was provided.");
            Environment.Exit(1);
        }
        InitializeFromFile(envFilePath);
        Migration(Console.WriteLine).Wait();
    }

    public static void MigrateAndInitialize()
    {
        InitializeFromEnvVar();
        Migration((s) => Log.Out(s)).Wait();
        ServerStartup();
    }
}
