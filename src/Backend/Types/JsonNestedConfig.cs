using System.Text.RegularExpressions;

namespace Conductor.Types;

public sealed class JsonNestingConfig
{
    private readonly HashSet<string> nestedProperties;
    private readonly List<Regex> nestedPatterns;
    private readonly HashSet<string> excludedProperties;

    public static readonly JsonNestingConfig Default = new JsonNestingConfig()
        .AddNestedProperty("addresses")
        .AddNestedProperty("storekeeper")
        .AddNestedProperty("items")
        .AddNestedProperty("details")
        .AddNestedPattern(@".*[Ll]ist$")
        .AddNestedPattern(@".*[Aa]rray$");

    public JsonNestingConfig()
    {
        nestedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        nestedPatterns = [];
        excludedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public JsonNestingConfig AddNestedProperty(string propertyName)
    {
        nestedProperties.Add(propertyName);
        return this;
    }

    public JsonNestingConfig AddNestedPattern(string pattern)
    {
        nestedPatterns.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
        return this;
    }

    public JsonNestingConfig ExcludeProperty(string propertyName)
    {
        excludedProperties.Add(propertyName);
        return this;
    }

    public bool ShouldNestProperty(string propertyName)
    {
        if (excludedProperties.Contains(propertyName))
            return false;

        if (nestedProperties.Contains(propertyName))
            return true;

        return nestedPatterns.Any(pattern => pattern.IsMatch(propertyName));
    }

    public static JsonNestingConfig FromQueryParameters(IQueryCollection? query)
    {
        var config = new JsonNestingConfig();

        if (query == null) return Default;

        if (query.TryGetValue("nestProperties", out var nestProps))
        {
            foreach (var prop in nestProps.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                config.AddNestedProperty(prop.Trim());
            }
        }

        if (query.TryGetValue("nestPatterns", out var nestPatterns))
        {
            foreach (var pattern in nestPatterns.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    config.AddNestedPattern(pattern.Trim());
                }
                catch { }
            }
        }

        if (query.TryGetValue("excludeProperties", out var excludeProps))
        {
            foreach (var prop in excludeProps.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                config.ExcludeProperty(prop.Trim());
            }
        }

        return config;
    }
}