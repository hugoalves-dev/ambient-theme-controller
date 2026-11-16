using System.Security.Cryptography;
using System.Text;
using AmbientTheme.Core.Settings;

namespace AmbientTheme.Windows;

/// <summary>Per-user secrets protected with Windows DPAPI. Values are never logged or exported.</summary>
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    public string? Read(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Write(string name, string secret)
    {
        var path = PathFor(name);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));
    }

    public void Delete(string name) => File.Delete(PathFor(name));

    private string PathFor(string name)
    {
        if (name.Length == 0 || !name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException("Secret names may only contain letters, digits, '-' and '_'.", nameof(name));
        }

        return Path.Combine(directory, name + ".bin");
    }
}
