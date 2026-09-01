using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ArcherCharacterRoster",
    menuName = "Archery Trick Shot/Archer Character Roster")]
public sealed class ArcherCharacterRoster : ScriptableObject
{
    private const string ResourcesPath =
        "Archer3D/ArcherCharacterRoster";

    public const string SelectionPlayerPrefsKey =
        "ArcheryTrickShot.SelectedArcherCharacterId";

    [Tooltip(
        "Character used when the player has not selected one, or when a " +
        "previously saved selection is no longer present in the roster. " +
        "Changing this does NOT replace an existing saved player selection. " +
        "Use Tools > Archery Trick Shot > Characters > Use Roster Default to clear it.")]
    public string DefaultCharacterId = "khaem";

    [Tooltip(
        "Every playable archer profile. Add a profile here once; scenes and " +
        "levels resolve the active character automatically.")]
    public List<Archer3DRuntimeProfile> Profiles =
        new List<Archer3DRuntimeProfile>();

    private static ArcherCharacterRoster cachedDefault;

    public static ArcherCharacterRoster LoadDefault()
    {
        if (cachedDefault == null)
        {
            cachedDefault =
                Resources.Load<ArcherCharacterRoster>(
                    ResourcesPath);
        }

        return cachedDefault;
    }

    public Archer3DRuntimeProfile ResolveSelectedProfile()
    {
        if (Profiles == null)
        {
            Profiles =
                new List<Archer3DRuntimeProfile>();
        }

        string selectedId = GetSavedSelectionId();

        if (string.IsNullOrWhiteSpace(selectedId))
            selectedId = DefaultCharacterId;

        Archer3DRuntimeProfile selected =
            FindProfile(selectedId);

        if (selected != null)
            return selected;

        Archer3DRuntimeProfile fallback =
            FindProfile(DefaultCharacterId);

        if (fallback != null)
            return fallback;

        for (int index = 0;
             index < Profiles.Count;
             index++)
        {
            if (Profiles[index] != null)
                return Profiles[index];
        }

        Debug.LogError(
            "ArcherCharacterRoster contains no valid character profiles.",
            this);

        return null;
    }

    public Archer3DRuntimeProfile ResolveRosterDefaultProfile()
    {
        Archer3DRuntimeProfile fallback =
            FindProfile(DefaultCharacterId);

        if (fallback != null)
            return fallback;

        if (Profiles == null)
            return null;

        for (int index = 0;
             index < Profiles.Count;
             index++)
        {
            if (Profiles[index] != null)
                return Profiles[index];
        }

        return null;
    }

    public Archer3DRuntimeProfile FindProfile(
        string characterId)
    {
        if (Profiles == null ||
            string.IsNullOrWhiteSpace(
                characterId))
        {
            return null;
        }

        for (int index = 0;
             index < Profiles.Count;
             index++)
        {
            Archer3DRuntimeProfile profile =
                Profiles[index];

            if (profile != null &&
                profile.MatchesCharacterId(
                    characterId))
            {
                return profile;
            }
        }

        return null;
    }

    public bool HasSavedSelection()
    {
        return PlayerPrefs.HasKey(
            SelectionPlayerPrefsKey);
    }

    public string GetSavedSelectionId()
    {
        if (!HasSavedSelection())
            return string.Empty;

        return PlayerPrefs
            .GetString(
                SelectionPlayerPrefsKey,
                string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    public bool SelectCharacter(
        string characterId,
        bool saveImmediately = true)
    {
        Archer3DRuntimeProfile profile =
            FindProfile(characterId);

        if (profile == null)
            return false;

        PlayerPrefs.SetString(
            SelectionPlayerPrefsKey,
            profile.CharacterId);

        if (saveImmediately)
            PlayerPrefs.Save();

        InvalidateRuntimeSelectionCache();

        return true;
    }

    public void ClearSavedSelection(
        bool saveImmediately = true)
    {
        PlayerPrefs.DeleteKey(
            SelectionPlayerPrefsKey);

        if (saveImmediately)
            PlayerPrefs.Save();

        InvalidateRuntimeSelectionCache();
    }

    public static void InvalidateRuntimeSelectionCache()
    {
        Archer3DRuntimeProfile
            .InvalidateCachedDefault();
    }

    private void OnValidate()
    {
        if (Profiles == null)
        {
            Profiles =
                new List<Archer3DRuntimeProfile>();
        }

        DefaultCharacterId =
            string.IsNullOrWhiteSpace(
                DefaultCharacterId)
                ? ""
                : DefaultCharacterId.Trim().ToLowerInvariant();

        HashSet<string> ids =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        for (int index = Profiles.Count - 1;
             index >= 0;
             index--)
        {
            Archer3DRuntimeProfile profile =
                Profiles[index];

            if (profile == null)
                continue;

            string id =
                profile.CharacterId?.Trim();

            if (string.IsNullOrWhiteSpace(id) ||
                ids.Add(id))
            {
                continue;
            }

            Debug.LogWarning(
                "Duplicate archer CharacterId '" +
                id +
                "' in roster. The first profile wins.",
                this);
        }

        // Inspector edits must never leave a stale selected profile cached in
        // the same Editor session. This does not clear the player's explicit
        // saved selection; the editor menu command does that intentionally.
        InvalidateRuntimeSelectionCache();
    }
}
