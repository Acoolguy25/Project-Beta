using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// Builds <c>Data/VoiceBank.asset</c> from the script in <c>Audio/Voice/Source~/lines.tsv</c> and the clips generated
    /// next to it (<c>gen_voices.py</c> writes <c>Audio/Voice/&lt;id&gt;.ogg</c>). Rerun after editing a line or regenerating
    /// its audio; every line keeps the clip's length so the server can time lines without loading audio.
    /// </summary>
    public static class CH_VoiceAuthoring {
        public const string BankPath = CH_Authoring.Root + "/Data/VoiceBank.asset";
        const string VoiceFolder = CH_Authoring.Root + "/Audio/Voice/";
        const string ScriptPath = VoiceFolder + "Source~/lines.tsv";
        // Profiles heard through Mara's radio: their subtitles arrive with a burst of static.
        static readonly HashSet<string> RadioProfiles = new() { "mara_radio", "mara_far" };

        [MenuItem("Ryan/Classic Horror/Rebuild Voice Bank")]
        public static CH_VoiceBank Build() {
            string script = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScriptPath);
            if (!File.Exists(script)) throw new FileNotFoundException("The voice script is missing.", script);
            var lines = new List<CH_VoiceBank.Line>();
            var missing = new List<string>();
            foreach (string raw in File.ReadAllLines(script)) {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                string[] fields = raw.Split('\t');
                if (fields.Length < 4) throw new InvalidDataException($"lines.tsv: expected id, speaker, profile and text in \"{raw}\".");
                string id = fields[0].Trim();
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(VoiceFolder + id + ".ogg");
                if (clip == null) missing.Add(id);
                else ConfigureImport(clip);
                lines.Add(new CH_VoiceBank.Line {
                    id = id,
                    speaker = fields[1].Trim(),
                    text = fields[3].Trim(),
                    clip = clip,
                    radio = RadioProfiles.Contains(fields[2].Trim()),
                    seconds = clip != null ? clip.length : 3f
                });
            }
            if (missing.Count > 0)
                Debug.LogWarning($"Classic Horror: {missing.Count} voice lines have no clip yet (run gen_voices.py): {string.Join(", ", missing)}");

            var bank = AssetDatabase.LoadAssetAtPath<CH_VoiceBank>(BankPath);
            if (bank == null) {
                bank = ScriptableObject.CreateInstance<CH_VoiceBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }
            bank.lines = lines.ToArray();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"Classic Horror: voice bank has {lines.Count} lines.");
            return bank;
        }

        /// <summary>Speech is short and played often: decompressed on load, mono, and kept out of the scene's preload.</summary>
        static void ConfigureImport(AudioClip clip) {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) is not AudioImporter importer) return;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool changed = !importer.forceToMono || settings.loadType != AudioClipLoadType.DecompressOnLoad
                || settings.compressionFormat != AudioCompressionFormat.Vorbis;
            if (!changed) return;
            importer.forceToMono = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }
}
