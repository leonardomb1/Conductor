using System.Reflection;
using Conductor.Data;
using Conductor.Router;
using Conductor.Shared.Config;
using LinqToDB.Data;

namespace Conductor.Shared;

public static class Initializer
{
    public static void InitializeFromDotEnv(string envFilePath)
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
            Startup();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    public static void InitializeFromEnvVar()
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
            Startup();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    private static void Startup()
    {
        try
        {
            if (Settings.DevelopmentMode)
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            }

            // Linq2db config
            DataConnection.DefaultSettings = new ConnectionSettings();

            // EF Core initialization
            using var db = new EfContext();
            db.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }

        Server server = new();
        server.Run();
    }
}
