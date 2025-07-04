using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using NetTools;
using Serilog;

namespace Conductor.Shared;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ConfigKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}

public static class Settings
{
    public static Lazy<ParallelOptions> ParallelRule => new(() => new() { MaxDegreeOfParallelism = MaxDegreeParallel });

    public static Lazy<IPAddressRange[]> AllowedIpsRange => new(() =>
    {
        if (AllowedIps.IsNullOrEmpty()) return [];

        string[] ranges = AllowedIps.Split(SplitterChar);
        IPAddressRange[] arrayOfRanges = new IPAddressRange[ranges.Length];

        for (byte i = 0; i < ranges.Length; i++)
        {
            if (!IPAddressRange.TryParse(ranges[i], out var pRange))
            {
                Log.Error($"Invalid IP address range configured: {ranges[i]}");
                continue;
            }
            arrayOfRanges[i] = pRange;
        }

        return arrayOfRanges;
    });

    public static Lazy<HashSet<string>> AllowedCorsSet => new(() => AllowedCors?.Split(SplitterChar).ToHashSet() ?? []);

    public static Lazy<JsonSerializerOptions> JsonSOptions => new(() => new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    });

    [ConfigKey("JOB_ROUTINE_DUMP_TIME_MS")]
    public static UInt32 JobRoutineDumpTime { get; set; }

    [ConfigKey("MAX_DEGREE_PARALLEL")]
    public static byte MaxDegreeParallel { get; set; }

    [ConfigKey("CSV_OUTPUT_PATH")]
    public static string CsvOutputPath { get; set; } = "";

    [ConfigKey("MAX_CONCURRENT_CONNECTIONS")]
    public static UInt16 MaxConcurrentConnections { get; set; }

    [ConfigKey("MAX_CONSUMER_FETCH")]
    public static UInt16 ConsumerFetchMax { get; set; }

    [ConfigKey("MAX_PRODUCER_LINECOUNT")]
    public static UInt64 ProducerLineMax { get; set; }

    [ConfigKey("MAX_FETCHING_LINECOUNT")]
    public static UInt64 FetcherLineMax { get; set; }

    [ConfigKey("SESSION_TIME_SEC")]
    public static UInt32 SessionTime { get; set; }

    [ConfigKey("QUERY_TIMEOUT_SEC")]
    public static Int32 QueryTimeout { get; set; }

    [ConfigKey("MAX_PIPELINE_ATTEMPT")]
    public static byte PipelineAttemptMax { get; set; }

    [ConfigKey("PORT_NUMBER")]
    public static Int32 PortNumber { get; set; }

    [ConfigKey("LDAP_PORT")]
    public static Int32 LdapPort { get; set; }

    [ConfigKey("CONNECTION_STRING")]
    public static string ConnectionString { get; set; } = "";

    [ConfigKey("LDAP_SSL")]
    public static bool LdapSsl { get; set; }

    [ConfigKey("USE_HTTPS")]
    public static bool UseHttps { get; set; }

    [ConfigKey("LDAP_VERIFY_CERTIFICATE")]
    public static bool LdapVerifyCertificate { get; set; }

    [ConfigKey("DB_TYPE")]
    public static string DbType { get; set; } = "";

    [ConfigKey("ENCRYPT_KEY")]
    public static string EncryptionKey { get; set; } = "";

    [ConfigKey("LDAP_SERVER")]
    public static string LdapServer { get; set; } = "";

    [ConfigKey("API_KEY")]
    public static string ApiKey { get; set; } = "";

    [ConfigKey("LDAP_DOMAIN")]
    public static string LdapDomain { get; set; } = "";

    [ConfigKey("LDAP_BASEDN")]
    public static string LdapBaseDn { get; set; } = "";

    [ConfigKey("LDAP_GROUPS")]
    public static string LdapGroups { get; set; } = "";

    [ConfigKey("NODES")]
    public static string Nodes { get; set; } = "";

    [ConfigKey("ALLOWED_IP_ADDRESSES")]
    public static string AllowedIps { get; set; } = "";

    [ConfigKey("ALLOWED_CORS")]
    public static string AllowedCors { get; set; } = "";

    [ConfigKey("LDAP_GROUPDN")]
    public static string LdapGroupDN { get; set; } = "";

    [ConfigKey("REQUIRE_AUTHENTICATION")]
    public static bool RequireAuthentication { get; set; }

    [ConfigKey("CONNECTION_TIMEOUT_MIN")]
    public static Int32 ConnectionTimeout { get; set; }

    [ConfigKey("RESPONSE_CACHING_LIMIT_MB")]
    public static Int32 ResponseCachingLimit { get; set; }

    [ConfigKey("PIPELINE_BACKOFF_BASE_MS")]
    public static Int32 PipelineBackoff { get; set; }

    [ConfigKey("SPLITTER_CHAR")]
    public static string SplitterChar { get; set; } = "";

    [ConfigKey("CERTIFICATE_PASSWORD")]
    public static string CertificatePassword { get; set; } = "";

    [ConfigKey("CERTIFICATE_PATH")]
    public static string CertificatePath { get; set; } = "";

    [ConfigKey("ENCRYPT_INDICATOR_BEGIN")]
    public static string EncryptIndicatorBegin { get; set; } = "";

    [ConfigKey("ENCRYPT_INDICATOR_END")]
    public static string EncryptIndicatorEnd { get; set; } = "";

    [ConfigKey("DEVELOPMENT_MODE")]
    public static bool DevelopmentMode { get; set; }

    [ConfigKey("VERIFY_TCP")]
    public static bool VerifyTCP { get; set; }

    [ConfigKey("VERIFY_CORS")]
    public static bool VerifyCors { get; set; }
}
