namespace Conductor.Shared;

public static class Colors
{
    public static readonly string NL = Environment.NewLine;

    public static readonly string NORMAL = Console.IsOutputRedirected ? "" : "\x1b[39m";

    public static readonly string RED = Console.IsOutputRedirected ? "" : "\x1b[91m";

    public static readonly string GREEN = Console.IsOutputRedirected ? "" : "\x1b[92m";

    public static readonly string YELLOW = Console.IsOutputRedirected ? "" : "\x1b[93m";

    public static readonly string BLUE = Console.IsOutputRedirected ? "" : "\x1b[94m";

    public static readonly string MAGENTA = Console.IsOutputRedirected ? "" : "\x1b[95m";

    public static readonly string CYAN = Console.IsOutputRedirected ? "" : "\x1b[96m";

    public static readonly string GREY = Console.IsOutputRedirected ? "" : "\x1b[97m";

    public static readonly string BOLD = Console.IsOutputRedirected ? "" : "\x1b[1m";

    public static readonly string NOBOLD = Console.IsOutputRedirected ? "" : "\x1b[22m";

    public static readonly string UNDERLINE = Console.IsOutputRedirected ? "" : "\x1b[4m";

    public static readonly string NOUNDERLINE = Console.IsOutputRedirected ? "" : "\x1b[24m";

    public static readonly string REVERSE = Console.IsOutputRedirected ? "" : "\x1b[7m";

    public static readonly string NOREVERSE = Console.IsOutputRedirected ? "" : "\x1b[27m";
}