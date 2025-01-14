namespace Conductor.Shared.Config;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ConfigKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}