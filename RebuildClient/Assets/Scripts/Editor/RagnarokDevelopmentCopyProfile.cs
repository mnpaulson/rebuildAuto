using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Assets.Editor
{
    [Serializable]
    public sealed class RagnarokDevelopmentCopyProfile
    {
        public string name = "";
        public string description = "";
        public bool all;
        public RagnarokDevelopmentCopyResources resources = new RagnarokDevelopmentCopyResources();

        [NonSerialized] public string AssetPath = "";

        public void EnsureDefaults()
        {
            resources ??= new RagnarokDevelopmentCopyResources();
            resources.EnsureDefaults();
        }
    }

    [Serializable]
    public sealed class RagnarokDevelopmentCopyResources
    {
        public List<string> maps = new List<string>();
        public List<int> items = new List<int>();
        public List<string> jobs = new List<string>();
        public List<string> monsters = new List<string>();
        public List<int> npcs = new List<int>();
        public List<string> effects = new List<string>();
        public List<int> skills = new List<int>();

        public void EnsureDefaults()
        {
            maps ??= new List<string>();
            items ??= new List<int>();
            jobs ??= new List<string>();
            monsters ??= new List<string>();
            npcs ??= new List<int>();
            effects ??= new List<string>();
            skills ??= new List<int>();
        }
    }

    public sealed class RagnarokDevelopmentCopyWindow : EditorWindow
    {
        private const string ProfileDirectory = "Assets/Scripts/Editor/DevelopmentCopyProfiles";

        private readonly List<RagnarokDevelopmentCopyProfile> profiles = new List<RagnarokDevelopmentCopyProfile>();
        private readonly List<string> validationErrors = new List<string>();
        private string[] profileNames = Array.Empty<string>();
        private Vector2 scrollPosition;
        private int selectedProfile;

        [MenuItem("Ragnarok/Development Copy Profiles", priority = 2)]
        public static void Open()
        {
            Open(null);
        }

        [MenuItem("Ragnarok/Minimal Development Copy", priority = 2)]
        public static void OpenMinimum()
        {
            Open("minimum.json");
        }

        private static void Open(string preferredFile)
        {
            var window = GetWindow<RagnarokDevelopmentCopyWindow>("Development Copy");
            window.LoadProfiles(preferredFile);
            window.Show();
        }

        private void OnEnable()
        {
            LoadProfiles(null);
        }

        private void LoadProfiles(string preferredFile)
        {
            profiles.Clear();

            if (Directory.Exists(ProfileDirectory))
            {
                foreach (var path in Directory.GetFiles(ProfileDirectory, "*.json", SearchOption.TopDirectoryOnly)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var profile = JsonUtility.FromJson<RagnarokDevelopmentCopyProfile>(File.ReadAllText(path));
                        if (profile == null)
                            continue;
                        profile.EnsureDefaults();
                        profile.AssetPath = path.Replace("\\", "/");
                        profiles.Add(profile);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"Could not load development copy profile '{path}': {exception.Message}");
                    }
                }
            }

            profileNames = profiles.Select(profile => string.IsNullOrWhiteSpace(profile.name)
                ? Path.GetFileNameWithoutExtension(profile.AssetPath)
                : profile.name).ToArray();

            if (!string.IsNullOrWhiteSpace(preferredFile))
            {
                var index = profiles.FindIndex(profile => string.Equals(
                    Path.GetFileName(profile.AssetPath), preferredFile, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    selectedProfile = index;
            }

            selectedProfile = Mathf.Clamp(selectedProfile, 0, Math.Max(0, profiles.Count - 1));
            ValidateSelectedProfile();
            Repaint();
        }

        private void ValidateSelectedProfile()
        {
            validationErrors.Clear();
            if (profiles.Count == 0)
                return;

            var profile = profiles[selectedProfile];
            validationErrors.AddRange(RagnarokCopyFromRealClient.ValidateDevelopmentProfile(profile));
            if (profiles.Count(candidate => string.Equals(candidate.name, profile.name, StringComparison.OrdinalIgnoreCase)) > 1)
                validationErrors.Add($"Profile name '{profile.name}' is duplicated.");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ragnarok Development Copy", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Profiles copy only their selected resources and required dependencies. Existing assets are never removed.",
                MessageType.Info);

            if (profiles.Count == 0)
            {
                EditorGUILayout.HelpBox($"No JSON profiles were found in {ProfileDirectory}.", MessageType.Error);
                if (GUILayout.Button("Refresh"))
                    LoadProfiles(null);
                return;
            }

            var newSelection = EditorGUILayout.Popup("Profile", selectedProfile, profileNames);
            if (newSelection != selectedProfile)
            {
                selectedProfile = newSelection;
                ValidateSelectedProfile();
            }
            var profile = profiles[selectedProfile];

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("File", profile.AssetPath);
            EditorGUILayout.LabelField("Description", profile.description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            if (profile.all)
            {
                EditorGUILayout.HelpBox("This profile performs an exhaustive full client-data copy.", MessageType.Warning);
            }
            else
            {
                var resources = profile.resources;
                EditorGUILayout.LabelField("Maps", resources.maps.Count.ToString());
                EditorGUILayout.LabelField("Items", resources.items.Count.ToString());
                EditorGUILayout.LabelField("Jobs", resources.jobs.Count.ToString());
                EditorGUILayout.LabelField("Monsters", resources.monsters.Count.ToString());
                EditorGUILayout.LabelField("NPCs", resources.npcs.Count.ToString());
                EditorGUILayout.LabelField("Effects", resources.effects.Count.ToString());
                EditorGUILayout.LabelField("Skills", resources.skills.Count.ToString());
            }

            foreach (var error in validationErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Profiles", GUILayout.Height(28)))
                LoadProfiles(Path.GetFileName(profile.AssetPath));

            GUI.enabled = validationErrors.Count == 0;
            if (GUILayout.Button("Import Profile", GUILayout.Height(28)))
                RagnarokCopyFromRealClient.CopyDevelopmentProfile(profile);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }
    }
}
