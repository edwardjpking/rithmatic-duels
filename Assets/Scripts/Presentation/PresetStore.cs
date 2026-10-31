using System.IO;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Presets;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// Loads and saves the player's own bind point layouts and defences (DESIGN.md section 6.8) as JSON in
    /// Unity's per-user data folder (Application.persistentDataPath), outside the project, so they never end
    /// up in the repo. The shipped defaults are never written; they are added on load.
    /// </summary>
    public static class PresetStore
    {
        public const string FileName = "defences.json";

        public static string FilePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

        /// <summary>The defaults plus everything the player has saved. A missing or unreadable file gives just the defaults.</summary>
        public static PresetLibrary Load(RulesConfig config)
        {
            PresetLibrary own = null;
            try
            {
                if (File.Exists(FilePath)) own = JsonUtility.FromJson<PresetLibrary>(File.ReadAllText(FilePath));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Couldn't read saved defences from " + FilePath + ": " + e.Message);
            }
            return (own ?? new PresetLibrary()).WithDefaults(config);
        }

        /// <summary>Writes the player's own layouts and defences (not the defaults).</summary>
        public static void Save(PresetLibrary library)
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(FilePath, JsonUtility.ToJson(library.CustomOnly(), true));
        }
    }
}
