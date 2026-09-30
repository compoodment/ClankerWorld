using System.Text.Json;
using Godot;

namespace ClankerWorld.GodotClient.ClientState;

/// <summary>
/// Keeps the public registration metadata in Godot's per-user application
/// directory. Losing this small file does not leak a credential: the user must
/// pair again, while the non-exportable device key stays in the platform key
/// store.
/// </summary>
public sealed class OwnerDeviceRegistrationStore
{
    private const string RegistrationPath = "user://owner-device-registration.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string path;

    public OwnerDeviceRegistrationStore()
        : this(ProjectSettings.GlobalizePath(RegistrationPath))
    {
    }

    internal OwnerDeviceRegistrationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public OwnerDeviceRegistration? TryLoad(string expectedPublicKeyFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPublicKeyFingerprint);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var registration = JsonSerializer.Deserialize<OwnerDeviceRegistration>(File.ReadAllText(path), JsonOptions);
            if (registration?.Authority is null ||
                string.IsNullOrWhiteSpace(registration.Authority.ServerAuthorityId) ||
                string.IsNullOrWhiteSpace(registration.Authority.WorldId) ||
                string.IsNullOrWhiteSpace(registration.DeviceId) ||
                !string.Equals(
                    registration.PublicKeyFingerprint,
                    expectedPublicKeyFingerprint,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return registration;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(OwnerDeviceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Authority.ServerAuthorityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Authority.WorldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.DeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.PublicKeyFingerprint);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registration, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Forget()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
