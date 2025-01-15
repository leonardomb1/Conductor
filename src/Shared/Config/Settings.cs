using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conductor.Shared.Config;

public static class Settings
{
    public static Lazy<ParallelOptions> ParallelRule => new(() => new() { MaxDegreeOfParallelism = MaxDegreeParallel });

    public static Lazy<HashSet<string>> TcpAllowedIpsSet => new(() => TcpAllowedIpsString?.Split(SplitterChar).ToHashSet() ?? []);

    public static Lazy<HashSet<string>> HttpAllowedIpsSet => new(() => HttpAllowedIpsString?.Split(SplitterChar).ToHashSet() ?? []);

    public static Lazy<HashSet<string>> AllowedCorsSet => new(() => AllowedCorsString?.Split(SplitterChar).ToHashSet() ?? []);

    public static Lazy<JsonSerializerOptions> JsonSOptions => new(() => new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    });

    public static string ConnectionName => $"{Constants.ProgramName}.{DbType}";

    [ConfigKey("LOG_DUMP_TIME_SEC")]
    public static Int32 LogDumpTime { get; set; }

    [ConfigKey("MAX_DEGREE_PARALLEL")]
    public static Int32 MaxDegreeParallel { get; set; }

    [ConfigKey("MAX_CONCURRENT_CONNECTIONS")]
    public static Int32 MaxConcurrentConnections { get; set; }

    [ConfigKey("MAX_CONSUMER_FETCH")]
    public static Int32 ConsumerFetchMax { get; set; }

    [ConfigKey("MAX_PRODUCER_LINECOUNT")]
    public static UInt32 ProducerLineMax { get; set; }

    [ConfigKey("SESSION_TIME_SEC")]
    public static Int32 SessionTime { get; set; }

    [ConfigKey("BULK_TIMEOUT_SEC")]
    public static Int32 BulkCopyTimeout { get; set; }

    [ConfigKey("MAX_CONSUMER_ATTEMPT")]
    public static Int32 ConsumerAttemptMax { get; set; }

    [ConfigKey("ENABLE_LOG_DUMP")]
    public static bool Logging { get; set; }

    [ConfigKey("PORT_NUMBER")]
    public static Int32 PortNumber { get; set; }

    [ConfigKey("MAX_QUERY_PARAMS")]
    public static Int32 MaxQueryParams { get; set; }

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

    [ConfigKey("API_KEY")]
    public static string ApiKey { get; set; } = "";

    [ConfigKey("LDAP_SERVER")]
    public static string LdapServer { get; set; } = "";

    [ConfigKey("LDAP_DOMAIN")]
    public static string LdapDomain { get; set; } = "";

    [ConfigKey("LDAP_BASEDN")]
    public static string LdapBaseDn { get; set; } = "";

    [ConfigKey("LDAP_GROUPS")]
    public static string LdapGroups { get; set; } = "";

    [ConfigKey("NODES")]
    public static string Nodes { get; set; } = "";

    [ConfigKey("TCP_ALLOWED_IPS")]
    public static string TcpAllowedIpsString { get; set; } = "";

    [ConfigKey("HTTP_ALLOWED_IPS")]
    public static string HttpAllowedIpsString { get; set; } = "";

    [ConfigKey("LDAP_GROUPDN")]
    public static string LdapGroupDN { get; set; } = "";

    [ConfigKey("DEBUG_DETAILED_ERROR")]
    public static bool DebugDetailedError { get; set; }

    [ConfigKey("REQUIRE_AUTHENTICATION")]
    public static bool RequireAuthentication { get; set; }

    [ConfigKey("CONNECTION_TIMEOUT_MIN")]
    public static Int32 ConnectionTimeout { get; set; }

    [ConfigKey("RESPONSE_CACHING_LIMIT_MB")]
    public static Int32 ResponseCachingLimit { get; set; }

    [ConfigKey("ALLOWED_CORS")]
    public static string AllowedCorsString { get; set; } = "";

    [ConfigKey("SPLITTER_CHAR")]
    public static string SplitterChar { get; set; } = "";

    [ConfigKey("INDEX_FILEGROUP_NAME")]
    public static string IndexFileGroupName { get; set; } = "";

    [ConfigKey("DEVELOPMENT_MODE")]
    public static bool DevelopmentMode { get; set; }

    [ConfigKey("MAX_LOG_QUEUE_SIZE")]
    public static Int32 MaxLogQueueSize { get; set; }
}