using CentrED.IO.Models;

namespace CentrED.IO;

/// <summary>
/// Loads, stores, and persists named client connection profiles.
/// </summary>
public static class ProfileManager
{
    private const string ProfilesDir = "profiles";

    /// <summary>
    /// Gets the in-memory collection of known profiles.
    /// </summary>
    public static List<Profile> Profiles = new();

    static ProfileManager()
    {
        if (!Directory.Exists(ProfilesDir))
        {
            Directory.CreateDirectory(ProfilesDir);
        }
        foreach (var profileDir in Directory.EnumerateDirectories(ProfilesDir))
        {
            var profile = Profile.Deserialize(profileDir);
            if(profile != null)
                Profiles.Add(profile);
        }
    }

    /// <summary>
    /// Gets the available profile names.
    /// </summary>
    public static string[] ProfileNames => Profiles.Select(p => p.Name).ToArray();

    /// <summary>
    /// Gets the currently active profile.
    /// </summary>
    public static Profile ActiveProfile => Profiles.Find(p => p.Name == Config.Instance.ActiveProfile) ?? new Profile();

    /// <summary>
    /// Saves the active profile.
    /// </summary>
    /// <returns>The index of the saved profile.</returns>
    public static int Save()
    {
        return Save(ActiveProfile);
    }

    /// <summary>
    /// Saves or updates a profile and makes it active.
    /// </summary>
    /// <param name="newProfile">The profile to persist.</param>
    /// <returns>The index of the saved profile.</returns>
    public static int Save(Profile newProfile)
    {
        var index = Profiles.FindIndex(p => p.Name == newProfile.Name);
        if (index != -1)
        {
            var profile = Profiles[index];
            profile.Hostname = newProfile.Hostname;
            profile.Port = newProfile.Port;
            profile.Username = newProfile.Username;
            profile.Password = newProfile.Password;
            profile.ClientPath = newProfile.ClientPath;
            profile.Serialize(ProfilesDir);
        }
        else
        {
            Profiles.Add(newProfile);
            newProfile.Serialize(ProfilesDir);
            index = Profiles.Count - 1;
        }
        Config.Instance.ActiveProfile = newProfile.Name;
        return index;
    }
    
    /// <summary>
    /// Persists the static-filter portion of the active profile.
    /// </summary>
    public static void SaveStaticFilter()
    {
        ActiveProfile.SerializeStaticFilter(ProfilesDir);
    }
}