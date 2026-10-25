using System.Reflection;

namespace RtlTerminal;

public static class AppVersion
{
    public const string ProductName = "RtlTerminal (AI Edition)";
    public const string OriginalCreator = "Behnam Tajadini";
    public const string EnhancedBy = "Yahia Elhamzawy";

    // The SDK generates assembly metadata from <Version> in RtlTerminal.csproj.
    public static string Display { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];

    public static string FullTitle => $"{ProductName} v{Display}";
}
