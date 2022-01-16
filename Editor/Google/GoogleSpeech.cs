using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Unity.EditorCoroutines.Editor;

// TODO: Make sure EVERYTHING is cleared when API Key is cleared.
// TODO: Code cleanup.
public class GoogleSpeech : EditorWindow
{
    public const string QueryUrl = "https://texttospeech.googleapis.com/v1beta1/text:synthesize";
    public const string ListUrl = "https://texttospeech.googleapis.com/v1beta1/voices";

    string APIKey = "";
    string key = "";
    string desiredText = "Hello, World!";
    string lastSavePath = "";
    string savedPath = "";
    string savedAssetPath = "";

    readonly string[] encodingTypes = new string[] { "LINEAR16", "MP3", "MP3_64_KBPS", "OGG_OPUS", "MULAW", "ALAW" };
    readonly string[] inputTypes = new string[] { "Text", "SSML" };
    string[] languages = new string[] { };
    string[] genders = new string[] { };
    string[] voices = new string[] { };

    bool checkingKey = false;
    bool validAPIKey = false;
    bool isDownloading = false;
    bool showAudioProfiles = false;
    bool showPlaybackSettings = true;
    bool showVoiceSettings = true;
    bool useTimePointing = true;
    bool showPreviewOptions = false;
    bool playingAudioClip = false;

    float desiredVolumeGain = 0.0f;
    float desiredPitch = 1.0f;
    float desiredRate = 1.0f;
    int desiredSampleRate = 24000;

    int selectedLanguage = 0;
    int lastLanguage = 0;
    int selectedVoice = 0;
    int lastVoice = 0;
    int selectedEncoding = 2;
    int lastEncoding = 2;
    int selectedGender = 0;
    int lastGender = 0;
    int selectedInputType = 0;

    Dictionary<string, List<Tuple<string, string, int>>> AvailableVoices;
    readonly List<AudioProfile> AudioProfiles = new List<AudioProfile>() {
        new AudioProfile("Wearable", "wearable-class-device", "Smart watches and other wearables, like Apple Watch, Wear OS watch."),
        new AudioProfile("Handset", "handset-class-device", "Smartphones, like Google Pixel, Samsung Galaxy, Apple iPhone."),
        new AudioProfile("Headphone", "headphone-class-device", "Earbuds or headphones for audio playback, like Sennheiser headphones."),
        new AudioProfile("Small Bluetooth Speaker", "small-bluetooth-speaker-class-device", "Small home speakers, like Google Home Mini."),
        new AudioProfile("Medium Bluetooth Speaker", "medium-bluetooth-speaker-class-device", "Smart home speakers, like Google Home."),
        new AudioProfile("Large Home Speaker", "large-home-entertainment-class-device", "Home entertainment systems or smart TVs, like Google Home Max, LG TV."),
        new AudioProfile("Car Speaker", "large-automotive-class-device", "Car speakers."),
        new AudioProfile("Telephony", "telephony-class-application", "Interactive Voice Response (IVR) systems.")
    };

    AudioClip savedClip;
    UnityWebRequest webRequest;
    UnityEditorInternal.ReorderableList profileList;
    EditorCoroutine previewRoutine;
    Assembly AudioImporter;
    Type AudioUtil;

    /// <summary>
    /// Styles for all of the UI elements.
    /// </summary>
    class Styles
    {
        public static GUIContent WindowTitleContent = new GUIContent("Google Text To Speech", "Provides an interface for using Google Text To Speech directly within the Unity Editor.");
        public static GUIContent PlaybackSettings = new GUIContent("Playback Settings", "Adjust settings such as pitch, playback speed, and volume gain. These settings are NOT saved.");
        public static GUIContent PlaybackPitch = new GUIContent("Pitch", "Speaking pitch, in the range [-20.0, 20.0]. 20 means increase 20 semitones from the original pitch. -20 means decrease 20 semitones from the original pitch.");
        public static GUIContent PlaybackSpeed = new GUIContent("Speed", "Speaking rate/speed, in the range [0.25, 4.0]. 1.0 is the normal native speed supported by the specific voice. 2.0 is twice as fast, and 0.5 is half as fast. If unset(0.0), defaults to the native 1.0 speed. Any other values < 0.25 or > 4.0 will return an error.");
        public static GUIContent PlaybackGain = new GUIContent("Volume Gain", "Volume gain (in dB) of the normal native volume supported by the specific voice, in the range [-96.0, 16.0]. If unset, or set to a value of 0.0 (dB), will play at normal native signal amplitude. A value of -6.0 (dB) will play at approximately half the amplitude of the normal native signal amplitude. A value of +6.0 (dB) will play at approximately twice the amplitude of the normal native signal amplitude. Strongly recommend not to exceed +10 (dB) as there's usually no effective increase in loudness for any value greater than that.");
        public static GUIContent PlaybackSampleRate = new GUIContent("Sample Rate", "The synthesis sample rate (in hertz) for this audio. If this is different from the voice's natural sample rate, then the synthesizer will honor this request by converting to the desired sample rate (which might result in worse audio quality), unless the specified sample rate is not supported for the encoding chosen, in which case the request will fail.");
        public static GUIContent VoiceSettings = new GUIContent("Voice Settings", "Adjust voice settings such as language, gender, voice type, and encoding. These settings will be saved.");
        public static GUIContent VoiceEncoding = new GUIContent("Encoding", "The format of the audio byte stream.");
        public static GUIContent AudioProfiles = new GUIContent("Audio Profiles", "Select 'audio effects' profiles that are applied on (post synthesized) text to speech. Effects are applied on top of each other in the order they are given. These settings are NOT saved.");
        public static GUIContent AudioProfile_Item = new GUIContent();
        public static GUIContent TextInputType = new GUIContent("Type", "The input source, which is either plain text or SSML.");
        public static GUIContent InputTimePointing = new GUIContent("Time Pointing", "Whether timepoints are returned in the response. Timepoints will be saved as a json file with the same file name as the audio clip.");
        public static GUIContent Download = new GUIContent("Download", "Begin downloading the audio with the specified parameters. Upon completion, you will be asked where to save the audio.");
        public static GUIContent CancelDownload = new GUIContent("Cancel", "Abort the current download.");
        public static GUIContent PreviewOptions = new GUIContent("Preview Audio", "Preview the downloaded AudioClip, or easily select it in the Project Hierarchy.");
        public static GUIContent PreviewPlayClip = new GUIContent("Play", "Play the downloaded audio clip.");
        public static GUIContent PreviewStopClip = new GUIContent("Stop", "Stop playing the downloaded audio clip.");
        public static GUIContent PreviewSelectAsset = new GUIContent("Select Asset", "Select the downloaded audio in the Project Hierarchy.");
    }

    [MenuItem("Window/Google/Text To Speech")]
    public static void ShowWindow()
    {
        EditorWindow wnd = EditorWindow.GetWindow(typeof(GoogleSpeech));
        wnd.titleContent = Styles.WindowTitleContent;
    }

    void Awake()
    {
        validAPIKey = EditorPrefs.GetBool("google_tts_validkey", false);
        key = APIKey = EditorPrefs.GetString("google_tts_apikey", string.Empty);
        lastSavePath = EditorPrefs.GetString("google_tts_lastSavePath", string.Empty);
        
        if (validAPIKey)
            EditorCoroutineUtility.StartCoroutine(QueryList(), this);
    }

    void OnEnable()
    {
        profileList = new UnityEditorInternal.ReorderableList(AudioProfiles, typeof(AudioProfile), true, false, false, false) { drawElementCallback = DrawListItems };
        AudioImporter = typeof(AudioImporter).Assembly;
        AudioUtil = AudioImporter.GetType("UnityEditor.AudioUtil");
    }

    void OnDisable()
    {
        // Pretty pointless
        AudioUtil = null;
        AudioImporter = null;
    }

    void OnGUI()
    {
        GUILayout.Space(8);
        GUILayout.Label("API Key", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(checkingKey || validAPIKey);
        EditorGUILayout.BeginHorizontal();
        key = EditorGUILayout.TextArea(key);
        if(key != APIKey)
        {
            EditorPrefs.SetString("google_tts_apikey", key);
            APIKey = key;
        }

        if(GUILayout.Button("Check"))
            EditorCoroutineUtility.StartCoroutine(QueryList(), this);

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!validAPIKey || checkingKey);

        if(GUILayout.Button("Clear"))
            ClearAPIKey();

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Separator();

        EditorGUI.BeginDisabledGroup(!validAPIKey || checkingKey);

        showPlaybackSettings = EditorGUILayout.Foldout(showPlaybackSettings, Styles.PlaybackSettings, true);
        if (showPlaybackSettings)
        {
            GUILayout.Label(Styles.PlaybackPitch, EditorStyles.boldLabel);
            desiredPitch = EditorGUILayout.Slider(desiredPitch, -20.0f, 20.0f);
            EditorGUILayout.Separator();

            GUILayout.Label(Styles.PlaybackSpeed, EditorStyles.boldLabel);
            desiredRate = EditorGUILayout.Slider(desiredRate, 0.25f, 4.0f);
            EditorGUILayout.Separator();

            GUILayout.Label(Styles.PlaybackGain, EditorStyles.boldLabel);
            desiredVolumeGain = EditorGUILayout.Slider(desiredVolumeGain, -96.0f, 16.0f);

            GUILayout.Label(Styles.PlaybackSampleRate, EditorStyles.boldLabel);
            desiredSampleRate = EditorGUILayout.IntSlider(desiredSampleRate, 16000, 48000);
        }

        EditorGUILayout.Separator();

        showVoiceSettings = EditorGUILayout.Foldout(showVoiceSettings, Styles.VoiceSettings, true);
        if (showVoiceSettings)
        {
            GUILayout.Label("Language", EditorStyles.boldLabel);
            selectedLanguage = EditorGUILayout.Popup(selectedLanguage, languages);
            if (selectedLanguage != lastLanguage)
            {
                genders = GetGendersForLanguage(languages[selectedLanguage]);
                EditorPrefs.SetString("google_tts_selectedLanguage", languages[selectedLanguage]);
                lastLanguage = selectedLanguage;
                selectedGender = 0;
            }
            EditorGUILayout.Separator();

            GUILayout.Label("Gender", EditorStyles.boldLabel);
            selectedGender = EditorGUILayout.Popup(selectedGender, genders);
            if (selectedGender != lastGender)
            {
                voices = GetVoicesForGender(languages[selectedLanguage], genders[selectedGender]);
                EditorPrefs.SetString("google_tts_selectedGender", genders[selectedGender]);
                lastGender = selectedGender;
                selectedVoice = 0;
            }
            EditorGUILayout.Separator();

            GUILayout.Label("Voice", EditorStyles.boldLabel);
            selectedVoice = EditorGUILayout.Popup(selectedVoice, voices);
            if (selectedVoice != lastVoice)
            {
                EditorPrefs.SetString("google_tts_selectedVoice", voices[selectedVoice]);
                desiredSampleRate = GetNaturalSampleRateForVoice(languages[selectedLanguage], genders[selectedGender], voices[selectedVoice]);
                lastVoice = selectedVoice;
            }
            EditorGUILayout.Separator();

            GUILayout.Label(Styles.VoiceEncoding, EditorStyles.boldLabel);
            selectedEncoding = EditorGUILayout.Popup(selectedEncoding, encodingTypes);
            if (selectedEncoding != lastEncoding)
            {
                EditorPrefs.SetInt("google_tts_selectedEncoding", selectedEncoding);
                lastEncoding = selectedEncoding;
            }
        }

        EditorGUILayout.Separator();

        showAudioProfiles = EditorGUILayout.Foldout(showAudioProfiles, Styles.AudioProfiles, true);
        if (showAudioProfiles)
            profileList.DoLayoutList();

        EditorGUILayout.Separator();

        GUILayout.Label("Input", EditorStyles.boldLabel);
        selectedInputType = EditorGUILayout.Popup(Styles.TextInputType, selectedInputType, inputTypes);
        if (inputTypes[selectedInputType] == "SSML")
            useTimePointing = EditorGUILayout.Toggle(Styles.InputTimePointing, useTimePointing);
        desiredText = EditorGUILayout.TextArea(desiredText);
        EditorGUILayout.Separator();

        EditorGUI.BeginDisabledGroup(isDownloading);
        if (GUILayout.Button(Styles.Download))
            EditorCoroutineUtility.StartCoroutine(QueryVoice(), this);

        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!isDownloading);
        if (GUILayout.Button(Styles.CancelDownload))
            webRequest.Abort();

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(savedClip == null);
        showPreviewOptions = EditorGUILayout.Foldout(showPreviewOptions, Styles.PreviewOptions, true);
        if (showPreviewOptions)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(playingAudioClip);
            if (GUILayout.Button(Styles.PreviewPlayClip))
                PlayClipPreview(savedClip);
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!playingAudioClip);
            if (GUILayout.Button(Styles.PreviewStopClip))
                StopPreviewClip();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(Styles.PreviewSelectAsset))
                Selection.activeObject = savedClip;
        }

        EditorGUI.EndDisabledGroup();

        if(isDownloading)
        {
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Downloading...", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Mathf.RoundToInt(webRequest.downloadProgress * 100.0f)}%");
            GUILayout.EndHorizontal();
        }

        EditorGUI.EndDisabledGroup();

        if (checkingKey)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("Verifying API Key, please wait...", EditorStyles.boldLabel);
        }

    }

    IEnumerator QueryVoice()
    {
        AudioConfig audio_config = new AudioConfig {
            audioEncoding = encodingTypes[selectedEncoding],
            pitch = desiredPitch,
            speakingRate = desiredRate,
            sampleRateHertz = desiredSampleRate,
            volumeGainDb = desiredVolumeGain,
            effectsProfileId = GetDesiredEffects()
        };

        Voice voice_config = new Voice {
            languageCode = languages[selectedLanguage],
            name = voices[selectedVoice],
            ssmlGender = genders[selectedGender]
        };

        string queryData;
        if(inputTypes[selectedInputType] == "SSML")
        {
            TimepointType timepointing = useTimePointing ? TimepointType.SSML_MARK : TimepointType.TIMEPOINT_TYPE_UNSPECIFIED;
            queryData = EditorJsonUtility.ToJson(new VoiceQuerySSML {
                audioConfig = audio_config,
                input = new SSMLInput { ssml = desiredText },
                voice = voice_config,
                enableTimePointing = new TimepointType[] { timepointing }
            });
        }
        else
        {
            queryData = EditorJsonUtility.ToJson(new VoiceQueryText {
                audioConfig = audio_config,
                input = new TextInput { text = desiredText },
                voice = voice_config
            });
        }

        webRequest = new UnityWebRequest($"{QueryUrl}?key={APIKey}", UnityWebRequest.kHttpVerbPOST);
        byte[] bytes = Encoding.UTF8.GetBytes(queryData);
        webRequest.uploadHandler = new UploadHandlerRaw(bytes) {
            contentType = "application/json"
        };
        webRequest.downloadHandler = new DownloadHandlerBuffer();

        isDownloading = true;
        savedAssetPath = string.Empty;
        savedPath = string.Empty;
        savedClip = null;
        yield return webRequest.SendWebRequest();

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            QueryErrorResponse err = JsonUtility.FromJson<QueryErrorResponse>(webRequest.downloadHandler.text);
            EditorUtility.DisplayDialog($"Google Text To Speech - Error: {err.error.code}({err.error.status})", $"{err.error.message}", "OK");
        }
        else
        {
            SpeechResponse response = JsonUtility.FromJson<SpeechResponse>(webRequest.downloadHandler.text);

            string fileExt = GetFileExtensionFromEncoding(response.audioConfig.audioEncoding);
            string savePath = Application.dataPath;
            if (!string.IsNullOrWhiteSpace(lastSavePath) && Directory.Exists(lastSavePath))
                savePath = lastSavePath;

            savedPath = EditorUtility.SaveFilePanel("Select save location for speech audio file.", savePath, "Speech", fileExt);
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                lastSavePath = Path.GetDirectoryName(savedPath);
                EditorPrefs.SetString("google_tts_lastSavePath", lastSavePath);

                byte[] audioData = Convert.FromBase64String(response.audioContent);
                File.WriteAllBytes(savedPath, audioData);
                if(response.timepoints != null && response.timepoints.Length > 0)
                    File.WriteAllText(Path.ChangeExtension(savedPath, "json"), EditorJsonUtility.ToJson(new Timepoints { timepoints = response.timepoints }));

                AssetDatabase.Refresh();

                if (savedPath.StartsWith(Application.dataPath))
                {
                    savedAssetPath = savedPath.Remove(0, Application.dataPath.LastIndexOf('/') + 1);
                    savedClip = (AudioClip)EditorGUIUtility.Load(savedAssetPath);
                }
            }
            else
            {
                EditorUtility.DisplayDialog($"Google Text To Speech - Error", "No save file path was selected, the audio will not be saved.", "OK");
                savedPath = string.Empty;
            }
        }

        isDownloading = false;
    }

    IEnumerator QueryList()
    {
        UnityWebRequest request = UnityWebRequest.Get($"{ListUrl}?key={APIKey}");
        checkingKey = true;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            validAPIKey = false;
            QueryErrorResponse err = JsonUtility.FromJson<QueryErrorResponse>(request.downloadHandler.text);
            EditorUtility.DisplayDialog($"Google Text To Speech - Error: {err.error.code}({err.error.status})", $"{err.error.message}", "OK");
        }
        else
        {
            validAPIKey = true;            
            ListVoiceResponse response = JsonUtility.FromJson<ListVoiceResponse>(request.downloadHandler.text);
            PopulateVoices(response.voices);
            selectedLanguage = lastLanguage = GetLanguageIndex(EditorPrefs.GetString("google_tts_selectedLanguage", string.Empty));

            genders = GetGendersForLanguage(languages[selectedLanguage]);
            selectedGender = lastGender = GetGenderIndex(EditorPrefs.GetString("google_tts_selectedGender", string.Empty));

            voices = GetVoicesForGender(languages[selectedLanguage], genders[selectedGender]);
            selectedVoice = lastVoice = GetVoiceIndex(EditorPrefs.GetString("google_tts_selectedVoice", string.Empty));
            selectedEncoding = lastEncoding = EditorPrefs.GetInt("google_tts_selectedEncoding", 2);
        }

        EditorPrefs.SetBool("google_tts_validkey", validAPIKey);
        checkingKey = false;
    }

    IEnumerator CleanupClipPreview()
    {
        yield return new EditorWaitForSeconds(savedClip.length);
        if (playingAudioClip)
            StopPreviewClip();
    }

    void PopulateVoices(ListVoice[] voices)
    {
        Dictionary<string, List<Tuple<string, string, int>>> v = new Dictionary<string, List<Tuple<string, string, int>>>();

        foreach(ListVoice voice in voices)
        {
            foreach(string language in voice.languageCodes)
            {
                if(!v.ContainsKey(language))
                    v.Add(language, new List<Tuple<string, string, int>>());

                v[language].Add(new Tuple<string, string, int>(voice.name, voice.ssmlGender, voice.naturalSampleRateHertz));
            }
        }

        AvailableVoices = v;

        // maybe give this its own method...
        List<string> lang = new List<string>(AvailableVoices.Keys.Count);
        foreach(KeyValuePair<string, List<Tuple<string, string, int>>> kv in AvailableVoices)
            lang.Add(kv.Key);

        languages = lang.ToArray();
    }

    string[] GetVoicesForGender(string language, string gender) => AvailableVoices[language].Where(voice => voice.Item2 == gender).Select(voice => voice.Item1).ToArray();
    string[] GetGendersForLanguage(string language) => AvailableVoices[language].Select(voice => voice.Item2).ToArray();

    int GetNaturalSampleRateForVoice(string language, string gender, string voice) => AvailableVoices[language].Where(voice => voice.Item2 == gender).Where(v => v.Item1 == voice).Select(voice => voice.Item3).First();

    void ClearAPIKey()
    {
        EditorPrefs.DeleteKey("google_tts_validkey");
        EditorPrefs.DeleteKey("google_tts_apikey");
        EditorPrefs.DeleteKey("google_tts_selectedGender");
        EditorPrefs.DeleteKey("google_tts_selectedLanguage");
        EditorPrefs.DeleteKey("google_tts_selectedVoice");
        EditorPrefs.DeleteKey("google_tts_selectedEncoding");
        EditorPrefs.DeleteKey("google_tts_lastSavePath");

        validAPIKey = false;
        checkingKey = false;
        key = APIKey = string.Empty;
        voices = new string[] { };
        genders = new string[] { };
        languages = new string[] { };
        savedPath = string.Empty;
    }

    int GetLanguageIndex(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return 0;

        for (int i = 0; i < languages.Length; i++)
            if (languages[i] == language)
                return i;
        return 0;
    }

    int GetGenderIndex(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return 0;

        for (int i = 0; i < genders.Length; i++)
            if (genders[i] == gender)
                return i;
        return 0;
    }

    int GetVoiceIndex(string voice)
    {
        if (string.IsNullOrWhiteSpace(voice))
            return 0;

        for (int i = 0; i < voices.Length; i++)
            if (voices[i] == voice)
                return i;
        return 0;
    }

    string[] GetDesiredEffects()
    {
        List<string> effects = new List<string>();

        foreach(AudioProfile profile in AudioProfiles)
            if(profile.Enabled)
                effects.Add(profile.Class);

        return effects.ToArray();
    }

    string GetFileExtensionFromEncoding(string encoding)
    {
        switch (encoding)
        {
            default: return "wav";
            case "MP3_64_KBPS":
            case "MP3":
                return "mp3";
            case "OGG_OPUS":
                return "ogg";
        }
    }

    void DrawListItems(Rect rect, int index, bool isActive, bool isFocused)
    {
        AudioProfile element = AudioProfiles[index];
        element.Enabled = EditorGUI.Toggle(new Rect(rect.x, rect.y, 16, EditorGUIUtility.singleLineHeight), element.Enabled);

        Styles.AudioProfile_Item.text = element.Name;
        Styles.AudioProfile_Item.tooltip = element.Description;
        EditorGUI.LabelField(new Rect(rect.x + 16, rect.y, EditorGUIUtility.labelWidth + 8, EditorGUIUtility.singleLineHeight), Styles.AudioProfile_Item);
    }

    void PlayClipPreview(AudioClip clip, int startSample = 0, bool loop = false)
    {
        MethodInfo method = AudioUtil.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        method.Invoke(null, new object[] { clip, startSample, loop });
        playingAudioClip = true;
        previewRoutine = EditorCoroutineUtility.StartCoroutine(CleanupClipPreview(), this);
    }

    void StopPreviewClip()
    {
        MethodInfo method = AudioUtil.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public, null, new Type[] { }, null);
        method.Invoke(null, new object[] { });
        playingAudioClip = false;
        EditorCoroutineUtility.StopCoroutine(previewRoutine);
    }

    [Serializable]
    class AudioConfig
    {
        public string audioEncoding = "AUDIO_ENCODING_UNSPECIFIED";
        public float speakingRate = 1.0f;
        public float pitch = 1.0f;
        public float volumeGainDb = 0.0f;
        public int sampleRateHertz = 24000;
        public string[] effectsProfileId;
    }

    [Serializable]
    class TextInput
    {
        public string text;
    }

    [Serializable]
    class SSMLInput
    {
        public string ssml;
    }

    [Serializable]
    class Voice
    {
        public string languageCode;
        public string name;
        public string ssmlGender;
    }

    [Serializable]
    class VoiceQueryText
    {
        public AudioConfig audioConfig;
        public TextInput input;
        public Voice voice;
    }

    [Serializable]
    class VoiceQuerySSML
    {
        public AudioConfig audioConfig;
        public SSMLInput input;
        public Voice voice;
        public TimepointType[] enableTimePointing;
    }

    [Serializable]
    class SpeechResponse
    {
        public string audioContent;
        public Timepoint[] timepoints;
        public AudioConfig audioConfig;
    }

    [Serializable]
    public class Timepoint
    {
        public string markName;
        public float timeSeconds;
    }

    [Serializable]
    public class Timepoints
    {
        public Timepoint[] timepoints;
    }

    [Serializable]
    class QueryErrorResponse
    {
        public QueryError error;
    }

    [Serializable]
    class QueryError
    {
        public int code;
        public string message;
        public string status;
    }

    enum TimepointType
    {
        TIMEPOINT_TYPE_UNSPECIFIED,
        SSML_MARK
    }

    // List classes
    [Serializable]
    class ListVoiceResponse
    {
        public ListVoice[] voices;
    }

    [Serializable]
    class ListVoice
    {
        public string[] languageCodes;
        public string name;
        public string ssmlGender;
        public int naturalSampleRateHertz;
    }

    [Serializable]
    class AudioProfile
    {
        [SerializeField]
        public bool Enabled;

        [SerializeField]
        public string Name;

        [SerializeField]
        public string Description;

        [NonSerialized]
        public string Class;

        public AudioProfile(string name, string className, string description)
        {
            Name = name;
            Class = className;
            Description = description;
        } 

    }

}