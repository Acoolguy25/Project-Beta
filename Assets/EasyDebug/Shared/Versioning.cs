#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Profile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyDebug.Shared {
    /// <summary>
    /// Editor tool to advance the player version across all build targets AND
    /// all Build Profile assets in the project.
    ///
    /// Version format: {major}.{minor:D2}{patchLetter}
    ///   e.g. 0.01, 0.01a, 0.01b, ... 0.01z, then 0.02, 0.02a, 0.02b, ...
    ///
    /// HOW THIS REACHES EVERY PROFILE, EVEN UNSELECTED ONES:
    /// FindAllBuildProfiles() uses AssetDatabase.FindAssets("t:BuildProfile"),
    /// which returns every Build Profile asset in the project regardless of
    /// whether it's checked, selected, or currently active in the Build
    /// Profiles window. Nothing here depends on selection state.
    ///
    /// HOW THIS REACHES PER-PROFILE OVERRIDES WITHOUT SWITCHING PROFILES:
    /// A Build Profile with "Player Settings overrides" enabled holds its own
    /// override object. As of Unity 6.2, the documented way to reach it is
    /// BuildProfile.GetComponent&lt;PlayerSettings&gt;() (confirmed by a Unity
    /// team member here: https://discussions.unity.com/t/getting-player-settings-for-a-specific-build-profile/1693736).
    /// That call returns the profile's own PlayerSettings object if an
    /// override is enabled, or the global PlayerSettings object if it isn't
    /// (which is harmless to touch here since we already set the global
    /// value first — writing the same value to it again is a no-op).
    /// PlayerSettings itself is otherwise a static-only API, so the only way
    /// to write a specific instance is via SerializedObject/SerializedProperty,
    /// same as the community-confirmed workaround.
    /// As a safety net for other/older Unity 6 point releases where that
    /// method's behavior might differ, this still falls back to structurally
    /// scanning the profile's own SerializedObject for a child object
    /// reference that exposes a "bundleVersion" string field.
    /// No profile switching, no active-profile change, no platform reimport —
    /// it's a synchronous data edit on each profile's own settings object.
    ///
    /// The GLOBAL Player Settings (Edit > Project Settings > Player) is
    /// updated the normal way via PlayerSettings.bundleVersion.
    /// </summary>
    public class VersionBumperWindow : EditorWindow {
        private static readonly Regex VersionRegex = new Regex(@"^(\d+)\.(\d+)([a-z]?)$");

        private string _forceSetText = "";
        private Vector2 _profileScroll;

        [MenuItem("Tools/Version Bumper")]
        public static void ShowWindow() {
            GetWindow<VersionBumperWindow>("Version Bumper");
        }

        private void OnGUI() {
            GUILayout.Label("Player Version", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Current bundleVersion (global)", PlayerSettings.bundleVersion);
            EditorGUILayout.LabelField("Android bundleVersionCode", PlayerSettings.Android.bundleVersionCode.ToString());
            EditorGUILayout.LabelField("iOS buildNumber", PlayerSettings.iOS.buildNumber);
            EditorGUILayout.LabelField("tvOS buildNumber", PlayerSettings.tvOS.buildNumber);
            EditorGUILayout.LabelField("VisionOS buildNumber", PlayerSettings.VisionOS.buildNumber);
            EditorGUILayout.LabelField("WSA packageVersion", PlayerSettings.WSA.packageVersion?.ToString() ?? "(none)");

            EditorGUILayout.Space();

            if (GUILayout.Button("Advance Patch (a, b, c...) — All Profiles", GUILayout.Height(30))) {
                var (major, minor, letter) = ParseVersion(PlayerSettings.bundleVersion);
                string target = string.IsNullOrEmpty(letter) ? Format(major, minor, "a")
                              : letter == "z" ? Format(major, minor + 1, "")
                              : Format(major, minor, ((char)(letter[0] + 1)).ToString());
                ApplyEverywhere(target);
            }

            if (GUILayout.Button("Advance Version (0.01 -> 0.02) — All Profiles", GUILayout.Height(24))) {
                var (major, minor, _) = ParseVersion(PlayerSettings.bundleVersion);
                ApplyEverywhere(Format(major, minor + 1, ""));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Force Set Version", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _forceSetText = EditorGUILayout.TextField(_forceSetText);
            GUI.enabled = !string.IsNullOrWhiteSpace(_forceSetText);
            if (GUILayout.Button("Set — All Profiles", GUILayout.Width(120))) {
                string raw = _forceSetText.Trim();
                if (VersionRegex.IsMatch(raw) || EditorUtility.DisplayDialog(
                        "Non-standard version format",
                        $"\"{raw}\" doesn't match the {{major}}.{{minor}}{{letter}} pattern " +
                        "(e.g. 0.01 or 0.01a). Set it anyway?",
                        "Set anyway", "Cancel")) {
                    ApplyEverywhere(raw);
                }
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Overwrites bundleVersion directly. Expected format: 0.01 or 0.01a", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build Profiles Found", EditorStyles.boldLabel);
            var profiles = FindAllBuildProfiles();
            if (profiles.Length == 0) {
                EditorGUILayout.HelpBox("No Build Profile assets found in the project. Buttons apply to global Player Settings only.", MessageType.None);
            } else {
                _profileScroll = EditorGUILayout.BeginScrollView(_profileScroll, GUILayout.Height(Mathf.Min(120, profiles.Length * 20 + 10)));
                foreach (var p in profiles) {
                    EditorGUILayout.LabelField("• " + AssetDatabase.GetAssetPath(p));
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.HelpBox(
                    $"Found {profiles.Length} Build Profile asset(s) in the project — every one of them " +
                    "gets updated when you click a button above, regardless of selection state. Profiles " +
                    "with no Player Settings override just inherit the global value shown above. Check " +
                    "the Console log after clicking for exactly which profiles were written to and how.",
                    MessageType.Info);
            }
        }

        private static string Format(int major, int minor, string letter) => $"{major}.{minor:D2}{letter}";

        private static (int major, int minor, string letter) ParseVersion(string version) {
            var match = VersionRegex.Match(version);
            if (!match.Success) {
                throw new FormatException(
                    $"bundleVersion \"{version}\" doesn't match {{major}}.{{minor}}{{letter}} " +
                    "(e.g. 0.01 or 0.01a). Use Force Set to fix it, then try again.");
            }

            return (
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                match.Groups[3].Value
            );
        }

        /// <summary>
        /// Sets the given version on global PlayerSettings, plus on every
        /// Build Profile's own PlayerSettings override (if it has one) — all
        /// synchronously, in one pass, no active-profile switching.
        /// </summary>
        private static void ApplyEverywhere(string newVersion) {
            PlayerSettings.bundleVersion = newVersion;
            BumpGlobalPlatformBuildNumbers();

            var allProfiles = FindAllBuildProfiles();
            var touchedViaComponent = new List<string>();
            var touchedViaScan = new List<string>();
            var untouched = new List<string>();

            foreach (var profile in allProfiles) {
                string path = AssetDatabase.GetAssetPath(profile);
                var result = TryWriteBundleVersionOverride(profile, newVersion);
                switch (result) {
                    case WriteResult.WrittenViaComponent: touchedViaComponent.Add(path); break;
                    case WriteResult.WrittenViaScan: touchedViaScan.Add(path); break;
                    default: untouched.Add(path); break;
                }
            }

            AssetDatabase.SaveAssets();

            int totalTouched = touchedViaComponent.Count + touchedViaScan.Count;
            var msg = $"[VersionBumper] Set bundleVersion -> {newVersion} on global Player Settings, " +
                      $"{totalTouched}/{allProfiles.Length} profile(s) updated.";
            if (touchedViaComponent.Count > 0)
                msg += "\nVia GetComponent<PlayerSettings>():\n  " + string.Join("\n  ", touchedViaComponent);
            if (touchedViaScan.Count > 0)
                msg += "\nVia SerializedObject fallback scan:\n  " + string.Join("\n  ", touchedViaScan);
            if (untouched.Count > 0)
                msg += "\nNo override found (inherits global — this is expected if override is disabled):\n  " + string.Join("\n  ", untouched);

            Debug.Log(msg);
        }

        private enum WriteResult { WrittenViaComponent, WrittenViaScan, NotFound }

        private static WriteResult TryWriteBundleVersionOverride(BuildProfile profile, string newVersion) {
            // Primary path: the documented Unity 6.2+ entry point. Returns
            // either the profile's own override object, or the global
            // PlayerSettings object if no override is enabled (safe to
            // no-op write to, since global is already set above).
            UnityEngine.Object viaComponent = null;
            try {
                viaComponent = profile.GetComponent<PlayerSettings>();
            } catch (Exception e) {
                Debug.LogWarning($"[VersionBumper] GetComponent<PlayerSettings>() threw on " +
                                  $"{AssetDatabase.GetAssetPath(profile)}: {e.Message}. Falling back to scan.");
            }

            if (viaComponent != null && WriteBundleVersionOnObject(viaComponent, newVersion))
                return WriteResult.WrittenViaComponent;

            // Fallback: structurally scan the profile's own SerializedObject
            // for a child object reference that itself exposes a
            // "bundleVersion" string field, in case the primary path isn't
            // available or didn't find the field on this Editor version.
            var profileSo = new SerializedObject(profile);
            var prop = profileSo.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren)) {
                enterChildren = false; // only descend one level from the root per iteration below

                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null) {
                    if (WriteBundleVersionOnObject(prop.objectReferenceValue, newVersion))
                        return WriteResult.WrittenViaScan;
                }
            }

            return WriteResult.NotFound; // profile inherits global, or override layout not recognized
        }

        private static bool WriteBundleVersionOnObject(UnityEngine.Object target, string newVersion) {
            var so = new SerializedObject(target);
            var versionProp = so.FindProperty("bundleVersion");
            if (versionProp == null || versionProp.propertyType != SerializedPropertyType.String)
                return false;

            versionProp.stringValue = newVersion;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return true;
        }

        private static void BumpGlobalPlatformBuildNumbers() {
            PlayerSettings.Android.bundleVersionCode += 1;

            if (int.TryParse(PlayerSettings.iOS.buildNumber, out int iosBuild))
                PlayerSettings.iOS.buildNumber = (iosBuild + 1).ToString();
            else
                PlayerSettings.iOS.buildNumber = "1";

            if (int.TryParse(PlayerSettings.tvOS.buildNumber, out int tvosBuild))
                PlayerSettings.tvOS.buildNumber = (tvosBuild + 1).ToString();
            else
                PlayerSettings.tvOS.buildNumber = "1";

            if (int.TryParse(PlayerSettings.VisionOS.buildNumber, out int visionosBuild))
                PlayerSettings.VisionOS.buildNumber = (visionosBuild + 1).ToString();
            else
                PlayerSettings.VisionOS.buildNumber = "1";

            // WSA.packageVersion is a System.Version (major.minor.build.revision),
            // not a plain int/string like the others. Bump its "build" component.
            var wsaVersion = PlayerSettings.WSA.packageVersion;
            if (wsaVersion != null) {
                PlayerSettings.WSA.packageVersion = new Version(
                    Math.Max(wsaVersion.Major, 0),
                    Math.Max(wsaVersion.Minor, 0),
                    Math.Max(wsaVersion.Build, 0) + 1,
                    Math.Max(wsaVersion.Revision, 0));
            }
        }

        private static BuildProfile[] FindAllBuildProfiles() {
            return AssetDatabase.FindAssets("t:BuildProfile")
                .Select(guid => AssetDatabase.LoadAssetAtPath<BuildProfile>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(p => p != null)
                .ToArray();
        }
    }
}
#endif