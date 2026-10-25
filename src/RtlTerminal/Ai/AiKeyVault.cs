using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RtlTerminal.Ai;

public static class AiKeyVault
{
    private const string BaseRegistryKey = @"Software\RtlTerminal\Ai";
    private const string KeysSubRegistryKey = @"Software\RtlTerminal\Ai\Keys";

    /// <summary>
    /// Saves an API key encrypted with Windows DPAPI (CurrentUser scope).
    /// If the key is null or whitespace, the stored entry is removed.
    /// </summary>
    public static void SaveKey(AiProviderKind provider, string apiKey)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeysSubRegistryKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            key.DeleteValue(provider.ToString(), throwOnMissingValue: false);
            return;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        byte[] encryptedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        key.SetValue(provider.ToString(), encryptedBytes, RegistryValueKind.Binary);
    }

    /// <summary>
    /// Loads and decrypts an API key for the specified provider using Windows DPAPI.
    /// Returns null if no key is found or decryption fails.
    /// </summary>
    public static string? LoadKey(AiProviderKind provider)
    {
        try
        {
            // Primary location: HKCU\Software\RtlTerminal\Ai\Keys
            using (var key = Registry.CurrentUser.OpenSubKey(KeysSubRegistryKey))
            {
                if (key?.GetValue(provider.ToString()) is byte[] bytes && bytes.Length > 0)
                {
                    byte[] plainBytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }

            // Fallback location: HKCU\Software\RtlTerminal\Ai with "Key_{provider}"
            using (var baseKey = Registry.CurrentUser.OpenSubKey(BaseRegistryKey))
            {
                if (baseKey?.GetValue($"Key_{provider}") is byte[] fallbackBytes && fallbackBytes.Length > 0)
                {
                    byte[] plainBytes = ProtectedData.Unprotect(fallbackBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }
        catch
        {
            // DPAPI decryption can throw if user context changes or data is corrupted
        }

        return null;
    }

    /// <summary>
    /// Saves the current AI configuration to HKCU\Software\RtlTerminal\Ai.
    /// </summary>
    public static void SaveConfig(AiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        using var key = Registry.CurrentUser.CreateSubKey(BaseRegistryKey);
        key.SetValue("Provider", config.Provider.ToString(), RegistryValueKind.String);
        key.SetValue("SelectedModel", config.SelectedModel ?? string.Empty, RegistryValueKind.String);
        key.SetValue("AutoExecuteCommands", config.AutoExecuteCommands ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Loads the AI configuration from HKCU\Software\RtlTerminal\Ai.
    /// Returns a default configuration if not set or on read failure.
    /// </summary>
    public static AiConfig LoadConfig()
    {
        var config = new AiConfig();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(BaseRegistryKey);
            if (key != null)
            {
                if (key.GetValue("Provider") is string providerStr &&
                    Enum.TryParse<AiProviderKind>(providerStr, ignoreCase: true, out var provider))
                {
                    config.Provider = provider;
                }

                if (key.GetValue("SelectedModel") is string model)
                {
                    config.SelectedModel = model;
                }

                if (key.GetValue("AutoExecuteCommands") is int autoExec)
                {
                    config.AutoExecuteCommands = autoExec != 0;
                }
            }
        }
        catch
        {
            // Graceful fallback to default configuration
        }

        return config;
    }

    public static bool HasKey(AiProviderKind provider) =>
        !string.IsNullOrWhiteSpace(LoadKey(provider));

    public static bool HasApiKey(AiProviderKind provider) =>
        HasKey(provider);

    public static void SaveApiKey(AiProviderKind provider, string apiKey) =>
        SaveKey(provider, apiKey);

    public static string? LoadApiKey(AiProviderKind provider) =>
        LoadKey(provider);
}
