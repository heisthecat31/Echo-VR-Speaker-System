/**
 * iblowatsports
 * Date: 19 December 2020
 * Purpose: Run arena music process
 */

using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.IO.Compression;
using System.Text;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using SteamAudio;
using System.Net;
using System.ComponentModel;

public class SpeakersStart : MonoBehaviour
{
    // const, not a public field: as a serialised field the scene's stored value
    // overrode this and the app kept reporting the previous version.
    public const string VERSION_TAGNAME = "v0.4.5";

    /// <summary>
    /// Shown when GitHub has no release for this tag yet, or is unreachable. The
    /// popup used to come up blank in both of those cases.
    /// </summary>
    const string LOCAL_WHATS_NEW =
        "- Added a \"Pause when not in game\" setting - Windows pauses your music or video when Echo VR is not reporting an active match, and resumes it when you are back in\n" +
        "- Your selected app is now switched onto the virtual audio cable automatically when Echo Speaker System starts\n" +
        "- VB-CABLE is now detected as well as Virtual Audio Cable. If neither is installed, ESS recommends one and links the download\n" +
        "- Fixed the app hanging on startup with no window when the audio device could not be opened - it now explains the problem and lets you pick another input\n" +
        "- Every redirected app is now put back on its original audio device when ESS exits or crashes, not just the selected one\n" +
        "- Fixed spawn room logging that could add tens of MB to the Unity log file each session\n" +
        "- Much lower Echo VR API polling in standalone mode, and smoother listener movement\n" +
        "- Fixed a silent GoalHorn.wav permanently muting all audio after a goal\n" +
        "[Created by iblowatsports]";
    public float playerXAbsMult = 1.0f;
    public float tunnelEndMapDist = 40.0001f;
    public float maxTunnelMapDist = 90f;
    public float minVolumeMap = 0.01f;
    public float maxVolumeMap = 0.79f;
    public float spawnRoomVolumeFloor = 0.49f;
    public float spawnRoomLowPassFloor = 2000f;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    [DllImport("WinAudioDLL", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GetAudioEndpointInfo(StringBuilder str, int len);

    public AudioEndpoints GetAudioInfo()
    {
        StringBuilder str = new StringBuilder(16000);

        GetAudioEndpointInfo(str, 16000);

        return JsonUtility.FromJson<AudioEndpoints>(str.ToString());
    }
    public static System.IntPtr GetWindowHandle()
    {
        return GetActiveWindow();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern int MessageBox(IntPtr hwnd, String lpText, String lpCaption, uint uType);

    /// <summary>
    /// Shows Error alert box with OK button.
    /// </summary>
    /// <param name="text">Main alert text / content.</param>
    /// <param name="caption">Message box title.</param>
    public static void Error(string text, string caption)
    {
        try
        {
            MessageBox(GetWindowHandle(), text, caption, (uint)(0x00000000L | 0x00000010L));
        }
        catch { }
    }

    bool isAppListRefreshing = false;
    bool isGoalHornFileValid = false;
    public AudioSource Source1;
    string inputName = "";
    bool isGoalHornEnabled = true;
    bool isFirstAppInit = true;
    string appExeName = "";
    public bool hasCleanedUp = false;
    bool goalHornPlaying = false;

    Toggle goalHornToggle;
    List<AppEndpoint> originalAppEndpoints = new List<AppEndpoint>();
    AudioSource masterSpeaker;
    List<AudioSource> speakers = new List<AudioSource>();
    Dictionary<string, float> speakerDelays = new Dictionary<string, float>();
    Dictionary<string, AudioEchoFilter> speakerEchos = new Dictionary<string, AudioEchoFilter>();
    Dictionary<string, AudioReverbFilter> speakerReverbs = new Dictionary<string, AudioReverbFilter>();

    AudioEchoFilter masterSpeakerEcho;
    Dictionary<string, SteamAudioSource> steamAudioSpeakers = new Dictionary<string, SteamAudioSource>();
    public float speedOfSoundMultiplier = 1.39f;
    public float reverbLevel = 1.0f;
    int previousTimeSamples = 0;
    string latestReleaseURL = "";
    string latestReleaseVer = "";
    AudioReverbPreset globalReverbPreset, spawnRoomReverbPreset;
    int speakerCount = 18;
    bool reverseLoopOrder;
    float listenerVolume, goalHornClipVolMult = 0.0f;
    float goalHornVolumeUserMult = 1.1f;
    Slider goalHornVolMultSlider, goalHornTimeSlider, spawnRoomVolFloorSlider, spawnRoomLowPassFloorSlider;
    SpatialPlayerListener playerListener;
    SteamAudioListener steamAudioListener;
    AudioListener playerAudioListener;
    AudioLowPassFilter playerListernerLowPass;
    Dropdown.OptionData AudioInputData, AppSelectionData, ReverbPresetData;
    AudioEndpoints audioEndpointsJson = null;
    List<Dropdown.OptionData> AudioInputMessages = new List<Dropdown.OptionData>();
    List<Dropdown.OptionData> ReverbPresetMessages = new List<Dropdown.OptionData>();
    List<Dropdown.OptionData> AppSelectionMessages = new List<Dropdown.OptionData>();
    Dropdown AudioInputDropdown, AppSelectionDropdown, ReverbPresetDropdown, SpawnRoomReverbPresetDropdown;
    DropdownMouseOver AppSelectionDropdownMouseOver;
    int AudioInputIndex, AppSelectionIndex, ReverbPresetIndex;
    GameObject VACDownloadBtnGameObject, UpdateDownloadBtnGameObject;
    Button MSSettingsBtn, VACDownloadBtn, UpdateDownloadBtn, RefreshAppListBtn;
    /// <summary>
    /// Virtual audio cable products we can capture from, best recommendation first.
    /// Device names are matched on short distinctive tokens rather than in full, because
    /// Windows device names reach Unity truncated on some systems - "CABLE Output
    /// (VB-Audio Virtual Cable)" can arrive clipped at 32 characters.
    /// </summary>
    static readonly VirtualCable[] KnownVirtualCables = new VirtualCable[]
    {
        new VirtualCable(
            "VB-CABLE",
            "https://vb-audio.com/Cable/",
            "CABLE Input (VB-Audio Virtual Cable)",
            new string[] { "VB-Audio", "CABLE Output" }),
        new VirtualCable(
            "Virtual Audio Cable",
            "https://vac.muzychenko.net/",
            "Line 1 (Virtual Audio Cable)",
            new string[] { "Virtual Audio Cable" }),
    };
    const int FREQUENCY = 48000;
    const int MIC_START_TIMEOUT_MS = 5000;
    AudioClip masterClip, goalHornClip;
    float averageGoalHornLoudness, averageMusicLoudness, musicLoudnessAcc = 0.0f;
    long musicLoudnessCount = 0;
    int lastPos, pos;
    int loops;
    bool respawnResetDone;
    bool isReady,clipZeroed,isNewUpdate = false;
    public static string updateFileName = "";
    float goalHornMaxDuration = 23f;
    bool inSpawnRoom = false;
    Toggle pauseWhenNotInGameToggle;
    bool pauseWhenNotInGame = false;
    bool musicPausedForGame = false;
    // App ids reported by MediaControl.exe when we paused them, so we resume only
    // what we actually stopped and never restart something the user paused.
    List<string> pausedMediaApps = new List<string>();

    // Use this for initialization
    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        var config = AudioSettings.GetConfiguration();
        AudioSettings.Reset(config);
        MSSettingsBtn = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "OpenMSAppAudioSettingsBtn").GetComponent<Button>();
        goalHornVolMultSlider = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornVolMultSlider").GetComponent<Slider>();
        spawnRoomVolFloorSlider = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SpawnRoomVolumeFloorSlider").GetComponent<Slider>();
        goalHornTimeSlider = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornTimeSlider").GetComponent<Slider>();
        isNewUpdate = PlayerPrefs.GetString("RunningVersion", "") != VERSION_TAGNAME;
        globalReverbPreset = (AudioReverbPreset)PlayerPrefs.GetInt("GlobalReverbPreset", (int)AudioReverbPreset.Arena);
        spawnRoomReverbPreset = (AudioReverbPreset)PlayerPrefs.GetInt("SpawnRoomReverbPreset", (int)AudioReverbPreset.Arena);
        goalHornVolumeUserMult = PlayerPrefs.GetFloat("GoalHornVolumeUserMult", 1.1f);
        goalHornMaxDuration = PlayerPrefs.GetFloat("GoalHornMaxDuration", 23f);
        //PlayerPrefs.DeleteKey("SpawnRoomVolumeFloor");
        spawnRoomVolumeFloor = PlayerPrefs.GetFloat("SpawnRoomVolumeFloor", 0.5f);
        spawnRoomLowPassFloor = PlayerPrefs.GetFloat("spawnRoomLowPassFloor", 2000f);
        goalHornTimeSlider.value = goalHornMaxDuration;
        goalHornVolMultSlider.value = (goalHornVolumeUserMult - 0.5f)/0.05f;
        spawnRoomVolFloorSlider.value = (spawnRoomVolumeFloor)/0.05f;
        pauseWhenNotInGameToggle = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PauseWhenNotInGameToggle").GetComponent<Toggle>();
        pauseWhenNotInGame = PlayerPrefs.GetInt("PauseWhenNotInGame", 0) == 1;
        pauseWhenNotInGameToggle.isOn = pauseWhenNotInGame;
        pauseWhenNotInGameToggle.onValueChanged.AddListener(delegate
        {
            PauseWhenNotInGameChanged(pauseWhenNotInGameToggle);
        });
        StartCoroutine(GetWhatsNew());
        string[] args = System.Environment.GetCommandLineArgs();
        // Start at 1: args[0] is the executable path, and matching against it meant an
        // install directory that merely contained "reset" ("C:\\Presets\\...") wiped
        // every saved setting on launch.
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Contains("selectinput"))
            {
                ShowHideInputDropdown(true);
                break;
            }
            if (args[i].Contains("reset"))
            {
                PlayerPrefs.DeleteAll();
                break;
            }
        }
        goalHornToggle = GameObject.Find("GoalHornToggle").GetComponent<Toggle>();
        isGoalHornEnabled = PlayerPrefs.GetInt("GoalHornEnabled", 0) == 1;
        goalHornToggle.isOn = isGoalHornEnabled;
        goalHornToggle.onValueChanged.AddListener(delegate
        {
            GoalHornToggleValueChanged(goalHornToggle);
        });
        StartCoroutine(TryLoadGoalHornWav(isGoalHornEnabled));
        
        inputName = PlayerPrefs.GetString("InputName", "Line 1 (Virtual Audio Cable)");
        appExeName = PlayerPrefs.GetString("AppSourceName", "");
        audioEndpointsJson = GetAudioInfo();
        string endpointsJSON = PlayerPrefs.GetString("AppSourceOriginalEndpoints", "");
        if(endpointsJSON.Length > 0){
            AppAudioEndpoints AppEndPoints = JsonUtility.FromJson<AppAudioEndpoints>(endpointsJSON);
            foreach(AppEndpoint end in AppEndPoints.endpoints){
                ResetAppEndpoint(end.appName, end.originalEndpointID);
            }
            PlayerPrefs.DeleteKey("AppSourceOriginalEndpoints");
        }
        AudioInputDropdown = GameObject.Find("AudioSourceDropdown").GetComponent<Dropdown>();
        // Show the picker whenever the saved input is something we do not recognise as
        // a virtual cable, so a hand-picked device stays visible and adjustable.
        ShowHideInputDropdown(!IsVirtualCableDevice(inputName));
        AudioInputDropdown.ClearOptions();
        AppSelectionDropdown = GameObject.Find("AppSelectionDropdown").GetComponent<Dropdown>();
        ReverbPresetDropdown = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ReverbPresetDropdown").GetComponent<Dropdown>();
        SpawnRoomReverbPresetDropdown = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SpawnRoomReverbPresetDropdown").GetComponent<Dropdown>();
        VACDownloadBtnGameObject = GameObject.Find("OpenVACDownloadBtn");
        VACDownloadBtn = VACDownloadBtnGameObject.GetComponent<Button>();
        VACDownloadBtnGameObject.SetActive(false);
        UpdateDownloadBtnGameObject = GameObject.Find("DownloadUpdateBtn");
        UpdateDownloadBtn = UpdateDownloadBtnGameObject.GetComponent<Button>();
        UpdateDownloadBtnGameObject.SetActive(false);

        RefreshAppListBtn = GameObject.Find("RefreshAppListBtn").GetComponent<Button>();

        MSSettingsBtn.onClick.AddListener(delegate
        {
            OpenWinAppAudioSettings();
        });

        RefreshAppListBtn.onClick.AddListener(delegate
        {
            refreshAppList();
        });
        AppSelectionDropdownMouseOver = AppSelectionDropdown.GetComponent<DropdownMouseOver>();
        GameObject playerObject = GameObject.Find("Player Listener");
        playerListener = playerObject.GetComponent<SpatialPlayerListener>();
        
        if (!playerListener.isIgniteBotEmbedded)
        {
            StartCoroutine(GetLatestVer());
            isGoalHornEnabled = false;
            GameObject.Find("GoalHornToggle").SetActive(false);
            if (goalHornClip != null)
            {
                goalHornClip = null;
            }
        }
        playerAudioListener = playerObject.GetComponent<AudioListener>();
        steamAudioListener = playerAudioListener.GetComponent<SteamAudioListener>();
        playerListernerLowPass = playerAudioListener.GetComponent<AudioLowPassFilter>();
        spawnRoomLowPassFloorSlider = GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SpawnRoomLowPassFilterSlider").GetComponent<Slider>();
        spawnRoomLowPassFloorSlider.value = (spawnRoomLowPassFloor)/200f;
        if(spawnRoomLowPassFloor > 0f){
            playerListernerLowPass.enabled = true;
        }else{
            playerListernerLowPass.enabled = false;
        }
        masterSpeaker = GameObject.Find("MasterSpeaker").GetComponent<AudioSource>();
        // float mRandom = UnityEngine.Random.Range(0.92f,1.08f);
        // masterSpeaker.volume *= mRandom;
        // speakerDelays.Add(masterSpeaker.name, mRandom);
        // speakerEchos.Add(masterSpeaker.name, masterSpeaker.GetComponent<AudioEchoFilter>());
        for (int i = 1; i <= speakerCount; i++)
        {
            AudioSource speaker = GameObject.Find("Speaker " + i).GetComponent<AudioSource>();
            speaker.mute = true;
            speakers.Add(speaker);
            float random = UnityEngine.Random.Range(0.92f, 1.08f);
            speaker.volume *= UnityEngine.Random.Range(0.89f, 0.999f);
            speakerDelays.Add(speaker.name, UnityEngine.Random.Range(0.92f, 1.08f));
            speakerEchos.Add(speaker.name, speaker.GetComponent<AudioEchoFilter>());
        }
        for (int i = 1; i <= speakerCount; i++)
        {
            steamAudioSpeakers.Add("Speaker " + i, GameObject.Find("Speaker " + i).GetComponent<SteamAudioSource>());
            speakerReverbs.Add("Speaker " + i, GameObject.Find("Speaker " + i).GetComponent<AudioReverbFilter>());
        }
        foreach (var steamSourcePair in steamAudioSpeakers)
        {
            steamSourcePair.Value.reflections = false;
            float rand = UnityEngine.Random.Range(0.85f, 1.15f);
            steamSourcePair.Value.dipolePower *= rand;
            rand = ((rand - 1.0f) * -1f) + 1.0f;
            GameObject.Find(steamSourcePair.Key).GetComponent<AudioDistortionFilter>().distortionLevel *= rand;
            steamSourcePair.Value.dipoleWeight *= UnityEngine.Random.Range(0.85f, 1.15f);
        }
        foreach (AudioReverbFilter reverb in speakerReverbs.Values)
        {
            reverb.enabled = true;
        }
        bool defaultFound = false;
        string installedCableDevice = null;
        foreach (var device in Microphone.devices)
        {
            AudioInputData = new Dropdown.OptionData();
            AudioInputData.text = device;
            AudioInputMessages.Add(AudioInputData);
            AudioInputDropdown.options.Add(AudioInputData);
            AudioInputIndex = AudioInputMessages.Count - 1;
            if (device == inputName)
            {
                defaultFound = true;
                AudioInputDropdown.value = AudioInputIndex;
            }
            if (installedCableDevice == null && IsVirtualCableDevice(device))
            {
                installedCableDevice = device;
            }
            UnityEngine.Debug.Log("Name: " + device);
        }
        if (!defaultFound && installedCableDevice != null)
        {
            // The saved device is gone but a cable is installed, so adopt it instead of
            // failing. This is what lets a fresh VB-CABLE install work without the user
            // having to touch the input dropdown at all. Done here, before the
            // onValueChanged listener is attached below, so it cannot re-enter
            // sourceInit() while Start() is still running.
            inputName = installedCableDevice;
            PlayerPrefs.SetString("InputName", inputName);
            PlayerPrefs.Save();
            int adoptedIndex = AudioInputMessages.FindIndex(m => m.text == inputName);
            if (adoptedIndex >= 0)
            {
                AudioInputDropdown.value = adoptedIndex;
            }
            defaultFound = true;
            ShowHideInputDropdown(false);
        }
        AudioInputDropdown.onValueChanged.AddListener(delegate
        {
            InputDropdownValueChanged(AudioInputDropdown);
        });
        
        foreach (AudioReverbPreset preset in (AudioReverbPreset[]) Enum.GetValues(typeof(AudioReverbPreset)))
        {
            if (preset != AudioReverbPreset.User)
            {
                ReverbPresetData = new Dropdown.OptionData();
                ReverbPresetData.text = preset.ToString();
                ReverbPresetMessages.Add(ReverbPresetData);
                ReverbPresetDropdown.options.Add(ReverbPresetData);
                SpawnRoomReverbPresetDropdown.options.Add(ReverbPresetData);
                ReverbPresetIndex = ReverbPresetMessages.Count - 1;
                if (preset == globalReverbPreset)
                {
                    ReverbPresetDropdown.value = ReverbPresetIndex;
                }
                if(preset == spawnRoomReverbPreset){
                    SpawnRoomReverbPresetDropdown.value = ReverbPresetIndex;
                }
            }
        }
        ReverbPresetDropdown.onValueChanged.AddListener(delegate
        {
            ReverbPresetDropdownValueChanged(ReverbPresetDropdown);
        });
        SpawnRoomReverbPresetDropdown.onValueChanged.AddListener(delegate
        {
            SpawnRoomReverbPresetDropdownValueChanged(SpawnRoomReverbPresetDropdown);
        });

        AppSelectionDropdown.onValueChanged.AddListener(delegate
        {
            AppSelectionDropdownValueChanged(AppSelectionDropdown);
        });
        refreshAppList();
        AutoSwitchRememberedAppToCable();
        if (!defaultFound && installedCableDevice == null)
        {
            // No virtual audio cable of any kind is installed. Surface the download
            // button here; sourceInit() below raises the single dialog that names the
            // recommended cable and shows its link, so the same problem does not
            // produce two message boxes in a row.
            ShowHideInputDropdown(true);
            ShowCableDownloadButton();
        }

        sourceInit();
    }

    void GoalHornToggleValueChanged(Toggle change)
    {
        //isGoalHornEnabled = goalHornToggle.isOn;
        if(goalHornToggle.isOn){
            StartCoroutine(TryLoadGoalHornWav(true));
        }else{
            isGoalHornEnabled = false;
            goalHornClip = null;
        }
        PlayerPrefs.SetInt("GoalHornEnabled", isGoalHornEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }
    IEnumerator TryLoadGoalHornWav(bool enableIfValid)
    {
        string goalHornFilePath = "";
        isGoalHornEnabled = false;
        goalHornFilePath = Application.streamingAssetsPath + "\\..\\..\\GoalHorn.wav";
        string url = string.Format("file://{0}", goalHornFilePath);
        if (File.Exists(goalHornFilePath))
        {
            WWW www = new WWW(url);

            yield return www;
            try
            {
                goalHornClip = www.GetAudioClip(false, false);
                if (goalHornClip != null && goalHornClip.length > 0f)
                {
                    isGoalHornFileValid = true;
                    if(enableIfValid){
                        isGoalHornEnabled = true;
                        PlayerPrefs.SetInt("GoalHornEnabled", isGoalHornEnabled ? 1 : 0);
                        PlayerPrefs.Save();
                    }
                    float clipLoudness = 0.0f;
                    float[] sample = new float[(goalHornClip.samples) * goalHornClip.channels];
                    goalHornClip.GetData(sample, 0);
                    for (int i = 0; i < sample.Length; i++)
                    {
                        clipLoudness += Mathf.Abs(sample[i]);

                    }
                    UnityEngine.Debug.Log("GOALHORN: " + ((float)clipLoudness / (float)sample.Length).ToString());
                    averageGoalHornLoudness = (float)clipLoudness / (float)sample.Length;
                    UnityEngine.Debug.Log((float)clipLoudness / (float)sample.Length);

                }
            }
            catch
            {
                isGoalHornFileValid = false;
                goalHornToggle.enabled = false;
                isGoalHornEnabled = enableIfValid ? false : isGoalHornEnabled;
                goalHornToggle.isOn = false;
                goalHornToggle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornToggleBackground").GetComponent<Image>().color = new Color32(152, 147, 147, 255);
                goalHornToggle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornToggleLabel").GetComponent<Text>().color = new Color32(152, 147, 147, 255);
                GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "goalHornTooltipText").GetComponent<Text>().text = "C:\\Program Files(x86)\\Echo Speaker System\\GoalHorn.wav was not found/is invalid!";
            }
        }
        else
        {
            isGoalHornFileValid = false;
            goalHornToggle.enabled = false;
            isGoalHornEnabled = enableIfValid ? false : isGoalHornEnabled;
            goalHornToggle.isOn = false;
            var bg = goalHornToggle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornToggleBackground").GetComponent<Image>();
            bg.color = new Color32(152, 147, 147, 255);
            goalHornToggle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornToggleBackground").GetComponent<Image>().color = new Color32(152, 147, 147, 255);
            goalHornToggle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GoalHornToggleLabel").GetComponent<Text>().color = new Color32(152, 147, 147, 255);
            GameObject.Find("UICanvas").transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "goalHornTooltipText").GetComponent<Text>().text = "C:\\Program Files(x86)\\Echo Speaker System\\GoalHorn.wav was not found/is invalid!";
        }
    }

    void ShowHideInputDropdown(bool shouldShow)
    {
        if (!shouldShow)
        {
            AudioInputDropdown.enabled = false;
            AudioInputDropdown.interactable = false;
            GameObject.Find("AudioSourceDropdownLabel").GetComponent<Text>().enabled = false;
            GameObject.Find("AudioSourceArrow").GetComponent<Image>().enabled = false;
            AudioInputDropdown.image.enabled = false;
        }
        else
        {
            AudioInputDropdown.enabled = true;
            AudioInputDropdown.interactable = true;
            GameObject.Find("AudioSourceDropdownLabel").GetComponent<Text>().enabled = true;
            GameObject.Find("AudioSourceArrow").GetComponent<Image>().enabled = true;
            AudioInputDropdown.image.enabled = true;
        }
    }

    void sourceInit()
    {
        isReady = false;
        playerListener.speakersReady = false;
        masterSpeaker.clip = null;
        if (masterSpeaker.isPlaying)
        {
            masterSpeaker.Stop();
        }
        foreach (AudioSource aSource in speakers)
        {
            aSource.clip = null;
            if (aSource.isPlaying)
            {
                aSource.Stop();
            }
        }
        if (!Microphone.devices.Contains(inputName))
        {
            OnAudioInputUnavailable("Couldn't find audio device \"" + inputName + "\". Make sure your virtual audio cable is still installed, and that the output device of your music source is set to it in the Windows settings under 'App Volume and Device Settings'.");
            return;
        }
        masterClip = Microphone.Start(inputName, true, 300, FREQUENCY);
        reverseLoopOrder = false;
        loops = 0;
        //masterClip = AudioClip.Create("test", 300 * FREQUENCY, 1, FREQUENCY, false);
        // Wait for the capture device to hand us samples, but give up rather than spin
        // forever: a device that is present but never streams used to hang the whole app
        // here, with no window, no message and no way out but Task Manager.
        Stopwatch micStartTimer = Stopwatch.StartNew();
        while (!(Microphone.GetPosition(inputName) > 0))
        {
            if (micStartTimer.ElapsedMilliseconds > MIC_START_TIMEOUT_MS)
            {
                Microphone.End(inputName);
                masterClip = null;
                OnAudioInputUnavailable("Audio device \"" + inputName + "\" never started streaming. Pick a different input below, or reinstall your virtual audio cable.");
                return;
            }
            Thread.Sleep(1);
        }
        masterSpeaker.clip = masterClip;
        masterSpeaker.loop = true;
        masterSpeaker.dopplerLevel = 0f;
        foreach (AudioSource aSource in speakers)
        {
            aSource.dopplerLevel = 0.0f;
            aSource.clip = masterClip;
            aSource.loop = true;
        }
        StartCoroutine(SyncSourcesInit());
    }

    static VirtualCable RecommendedCable
    {
        get { return KnownVirtualCables[0]; }
    }

    /// <summary>
    /// The known virtual cable product a recording device belongs to, or null.
    /// </summary>
    static VirtualCable MatchVirtualCable(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return null;
        }
        foreach (VirtualCable cable in KnownVirtualCables)
        {
            foreach (string token in cable.deviceNameTokens)
            {
                if (deviceName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return cable;
                }
            }
        }
        return null;
    }

    static bool IsVirtualCableDevice(string deviceName)
    {
        return MatchVirtualCable(deviceName) != null;
    }

    /// <summary>
    /// First recording device belonging to a known virtual cable, or null if the user has
    /// none installed.
    /// </summary>
    static string FindInstalledCableDevice()
    {
        foreach (string device in Microphone.devices)
        {
            if (IsVirtualCableDevice(device))
            {
                return device;
            }
        }
        return null;
    }

    /// <summary>
    /// Reveals the download button and points it at the recommended cable, relabelling it
    /// to match so the button never advertises a product we are not linking to.
    /// </summary>
    void ShowCableDownloadButton()
    {
        if (VACDownloadBtnGameObject == null || VACDownloadBtn == null)
        {
            return;
        }
        VACDownloadBtnGameObject.SetActive(true);
        Text label = VACDownloadBtnGameObject.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = "Download " + RecommendedCable.displayName;
        }
        VACDownloadBtn.onClick.RemoveAllListeners();
        VACDownloadBtn.onClick.AddListener(delegate
        {
            OpenCableDownload();
        });
    }

    /// <summary>
    /// What to tell the user when no virtual cable is installed: which one to get, where
    /// to get it, and what to do once it is in.
    /// </summary>
    static string CableRecommendationText()
    {
        VirtualCable rec = RecommendedCable;
        return "No virtual audio cable was found. Echo Speaker System needs one to capture "
            + "the audio it plays through the arena speakers.\n\n"
            + "Recommended: " + rec.displayName + " (free)\n"
            + rec.downloadUrl + "\n\n"
            + "Install it, then set your music app's playback device to \""
            + rec.playbackDeviceHint + "\" in the Windows settings under 'App Volume and "
            + "Device Settings', and restart Echo Speaker System.\n\n"
            + "The \"Download " + rec.displayName + "\" button in the app opens this link.";
    }

    /// <summary>
    /// Called when the capture device cannot be opened. Leaves the app running and usable
    /// so the input can be re-picked, instead of hanging with no audio and no UI.
    /// </summary>
    void OnAudioInputUnavailable(string message)
    {
        isReady = false;
        if (playerListener != null)
        {
            playerListener.speakersReady = false;
        }
        ShowHideInputDropdown(true);
        if (FindInstalledCableDevice() == null)
        {
            // Nothing usable is installed, so follow the specific failure with the
            // recommendation and its link rather than leaving the user to work out
            // what to install.
            ShowCableDownloadButton();
            message += "\n\n" + CableRecommendationText();
        }
        Error(message, "Echo Speaker System - Audio Input");
    }

    /// <summary>
    /// Persists the crash-recovery record of which app we moved off which endpoint.
    /// JsonUtility cannot serialise a bare List&lt;T&gt; - it silently produces "{}" - so the
    /// list has to be wrapped in an object before it is written.
    /// </summary>
    void SaveOriginalAppEndpoints()
    {
        if (originalAppEndpoints.Count == 0)
        {
            PlayerPrefs.DeleteKey("AppSourceOriginalEndpoints");
        }
        else
        {
            PlayerPrefs.SetString("AppSourceOriginalEndpoints",
                JsonUtility.ToJson(new AppAudioEndpoints { endpoints = originalAppEndpoints }));
        }
        PlayerPrefs.Save();
    }

    void refreshAppList()
    {
        isAppListRefreshing = true;
        bool pastSelectedAppFound = false;
        audioEndpointsJson = GetAudioInfo();
        AppSelectionDropdown.ClearOptions();
        AppSelectionMessages = new List<Dropdown.OptionData>();
        AppSelectionIndex = 0;
        var appDropdownLabel = new Dropdown.OptionData("Select App to Be Played");
        AppSelectionDropdown.options.Insert(0, appDropdownLabel);
        AppSelectionMessages.Add(appDropdownLabel);
        AppSelectionDropdown.captionText.text = "Select App to Be Played";
        foreach (var endpoint in audioEndpointsJson.endpoints)
        {
            foreach (var session in endpoint.sessions)
            {
                if (!AppSelectionMessages.Any(m => m.text == session.exeName) && session.exeName != "Echo Speaker System" && session.exeName != "NVIDIA RTX Voice")
                {
                    AppSelectionData = new Dropdown.OptionData();
                    AppSelectionData.text = session.exeName;
                    AppSelectionMessages.Add(AppSelectionData);
                    AppSelectionDropdown.options.Add(AppSelectionData);
                    AppSelectionIndex = AppSelectionMessages.Count - 1;
                    if (session.exeName == appExeName)
                    {
                        pastSelectedAppFound = true;
                        AppSelectionDropdown.value = AppSelectionIndex;
                    }
                }
            }
        }
        if (!pastSelectedAppFound)
        {
            AppSelectionDropdown.value = 0;
        }
        AppSelectionDropdown.enabled = false;
        AppSelectionDropdown.enabled = true;
        AppSelectionDropdown.RefreshShownValue();
        isAppListRefreshing = false;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        //m_MyAudioSource = GetComponent<AudioSource>();
        if (playerListener.quitCalled)
        {
            Cleanup();
        }
        else
        {
            if (!hasCleanedUp && (pos = Microphone.GetPosition(inputName)) > (FREQUENCY / 2))
            {
                if (lastPos > pos) lastPos = 0;
                if (pos - lastPos > 0)
                {
                    // Allocate the space for the sample.
                    if (!goalHornPlaying && loops % 100 == 1)
                    {
                        float[] sample = new float[(pos - lastPos) * 1];

                        // Get the data from microphone.
                        masterClip.GetData(sample, lastPos);
                        float clipLoudness = 0f;
                        float highest = 0.0f;
                        int zeroCount = 0;
                        for (int i = 0; i < sample.Length; i++)
                        {
                            clipLoudness += Mathf.Abs(sample[i]);
                            if (sample[i] == 0.0f)
                            {
                                zeroCount++;
                            }
                            // if (sample[i] > highest)
                            // {
                            //     highest = sample[i];
                            // }
                            // sample[i] = sample[i] * 1.95f;
                            // if (sample[i] > 1.0f)
                            // {
                            //     UnityEngine.Debug.Log(sample[i]);
                            //     sample[i] = 1.0f;
                            // }
                        }
                        if (((float)zeroCount / (float)sample.Length) > 0.65f )
                        {
                            //UnityEngine.Debug.Log((float)zeroCount/(float)sample.Length);
                            // for (int i = 0; i < sample.Length; i++)
                            // {
                            //     sample[i] = 0.0f;
                            // }
                            // masterClip.SetData(sample, lastPos);
                            if(!clipZeroed){
                                StartCoroutine(StartFade(0.5f, 0));
                                clipZeroed = true;
                            }else{
                                StartCoroutine(StartFade(0.5f, 1));
                                clipZeroed = false;
                            }
                        }
                        else
                        {
                            if(clipZeroed){
                                StartCoroutine(StartFade(0.5f, 1));
                                clipZeroed = false;
                            }
                            //UnityEngine.Debug.Log(averageMusicLoudness);
                            float avgSampleLoudness = ((float)clipLoudness / (float)sample.Length);
                            if ((musicLoudnessAcc + avgSampleLoudness < float.MaxValue) && musicLoudnessCount + 1 < long.MaxValue)
                            {
                                musicLoudnessAcc += avgSampleLoudness;
                                musicLoudnessCount++;
                            }
                            else
                            {
                                musicLoudnessAcc = averageMusicLoudness + avgSampleLoudness;
                                musicLoudnessCount = 2;
                            }
                            averageMusicLoudness = musicLoudnessAcc / musicLoudnessCount;
                        }
                    }
                    if (isReady)
                    {
                        UpdateGameActivityPause();
                        if (playerListener.goalScored)
                        {
                            if(goalHornPlaying || !isGoalHornEnabled){
                                playerListener.goalScored = false;
                            }else{
                                // previousTimeSamples = masterSpeaker.timeSamples;
                                // speakerEchos[masterSpeaker.name].delay = 0f;
                                // masterSpeaker.Stop();
                                // masterSpeaker.volume *= 0.5f;
                                // masterSpeaker.clip = goalHornClip;
                                // masterSpeaker.loop = false;
                                if(clipZeroed){
                                    // StartCoroutine(StartFade(0.25f, 1));
                                    clipZeroed = false;
                                }
                                // Guard the divisor and clamp the result. A silent or
                                // near-silent GoalHorn.wav produced an infinite
                                // multiplier here, and the reciprocal applied when the
                                // horn finished then muted every speaker for the rest
                                // of the session. The round trip has to stay finite.
                                goalHornClipVolMult = (averageMusicLoudness == 0.0f || averageGoalHornLoudness <= 0.0f)
                                    ? 0.3f
                                    : Mathf.Clamp((averageMusicLoudness * goalHornVolumeUserMult) / averageGoalHornLoudness, 0.01f, 10.0f);
                                foreach (AudioSource aSource in speakers)
                                {
                                    // speakerEchos[aSource.name].delay = 0f;
                                    // aSource.Stop();
                                    aSource.volume *= goalHornClipVolMult;
                                    aSource.dopplerLevel = 0.005f;
                                    // aSource.clip = goalHornClip;
                                    aSource.loop = false;
                                }
                                StartCoroutine(SyncSourcesGoalHorn());
                                goalHornPlaying = true;
                                playerListener.goalScored = false;
                                StartCoroutine(SyncSourcesAfterGoal());
                            }
                        }
                        if (!reverseLoopOrder)
                        {
                            foreach (AudioSource aSource in speakers)
                            {
                                //aSource.clip.SetData(sample, lastPos);
                                //aSource.clip = masterClip;
                                float dist = UnityEngine.Vector3.Distance(aSource.transform.position, playerListener.transform.position) * (speedOfSoundMultiplier * speakerDelays[aSource.name]);//1.19f; //1.142f;
                                speakerEchos[aSource.name].delay = dist;
                                // if(loops > 300){
                                //     aSource.Pause();
                                // }
                                if (!aSource.isPlaying) { aSource.Play(); }
                                reverseLoopOrder = true;
                            }
                            //masterSpeaker.clip.SetData(sample, lastPos);
                            //masterSpeaker.clip = masterClip;
                            // float Mastdist = UnityEngine.Vector3.Distance(masterSpeaker.transform.position, playerListener.transform.position) * (speedOfSoundMultiplier * speakerDelays[masterSpeaker.name]);// 1.19f; ;
                            // speakerEchos[masterSpeaker.name].delay = Mastdist;
                            if (!masterSpeaker.isPlaying) { masterSpeaker.Play(); }
                        }
                        else
                        {
                            foreach (AudioSource aSource in Enumerable.Reverse(speakers))
                            {
                                //aSource.clip.SetData(sample, lastPos);
                                //aSource.clip = masterClip;
                                float dist = UnityEngine.Vector3.Distance(aSource.transform.position, playerListener.transform.position) * (speedOfSoundMultiplier * speakerDelays[aSource.name]);// 1.19f;
                                speakerEchos[aSource.name].delay = dist;
                                aSource.dopplerLevel = 0.005f;
                                // if(loops > 300){
                                //     aSource.Pause();
                                // }
                                if (!aSource.isPlaying) { aSource.Play(); }
                                //aSource.timeSamples = masterSpeaker.timeSamples;
                                reverseLoopOrder = false;
                            }
                            //masterSpeaker.clip.SetData(sample, lastPos);
                            //masterSpeaker.clip = masterClip;
                            // float Mastdist = UnityEngine.Vector3.Distance(masterSpeaker.transform.position, playerListener.transform.position) * (speedOfSoundMultiplier * speakerDelays[masterSpeaker.name]);//  1.19f;
                            // speakerEchos[masterSpeaker.name].delay = Mastdist;
                            if (!masterSpeaker.isPlaying) { masterSpeaker.Play(); }
                        }

                        float playerXAbs = Math.Abs(playerListener.head.position.x);
                        if (playerXAbs > 40f)
                        {
                            float vol = Map((playerXAbs*playerXAbsMult), tunnelEndMapDist, maxTunnelMapDist, minVolumeMap, maxVolumeMap);
                            AudioListener.volume = listenerVolume * (spawnRoomVolumeFloor + (Mathf.Log10(vol) / -4.0f));//41/(playerXAbs);// Mathf.Log10((41/(Math.Abs(playerListener.head.position.x)))*(41/(Math.Abs(playerListener.head.position.x))) * 20) - 0.29f; //
                            if(!inSpawnRoom){
                                foreach (AudioReverbFilter reverb in speakerReverbs.Values)
                                {
                                    reverb.reverbPreset = spawnRoomReverbPreset;
                                }
                                inSpawnRoom = true;
                            }
                            vol = Map(playerXAbs, 40.0001f, 76f, 0.0001f, 1.0f);
                            playerListernerLowPass.cutoffFrequency = spawnRoomLowPassFloor + ((Mathf.Log10(vol) / -4.0f) * 16000f);

                        }
                        else
                        {
                            //float vol2 = Map(40.001f, 40.0001f, 90f, 0.004f, 1.0f);
                            //AudioListener.volume = Mathf.Log10(vol) / -4.0f;//41/(playerXAbs);// Mathf.Log10((41/(Math.Abs(playerListener.head.position.x)))*(41/(Math.Abs(playerListener.head.position.x))) * 20) - 0.29f; //
                            //Debug.Log(Mathf.Log10(vol2) / -4.0f);
                            //UnityEngine.Debug.Log(listenerVolume);
                            if(inSpawnRoom){
                                foreach (AudioReverbFilter reverb in speakerReverbs.Values)
                                {
                                    reverb.reverbPreset = globalReverbPreset;
                                }
                                inSpawnRoom = false;
                            }
                            AudioListener.volume = listenerVolume;
                            playerListernerLowPass.cutoffFrequency = 22000f;
                        }
                        if (playerListener.head.position.x != -105.5 && (playerXAbs > 72))
                        {
                            if (!respawnResetDone)
                            {
                                if (!goalHornPlaying)
                                {
                                    StartCoroutine(SyncSources());
                                }
                                respawnResetDone = true;
                            }
                        }
                        else
                        {
                            respawnResetDone = false;
                        }
                        if (loops > 36000)
                        {
                            StartCoroutine(SyncSources());
                        }
                        else
                        {
                            loops++;
                        }
                    }

                    // Put the data in the audio source.


                    lastPos = pos;
                }
                if(AppSelectionDropdownMouseOver.isOver) {
                    AppSelectionDropdownMouseOver.isOver = false;
                    refreshAppList();
                }
            }

            // foreach(AudioSource aSource in speakers){
            //     //aSource.clip.SetData(sample, lastPos);
            //     // if(loops > 300){
            //     //     aSource.Pause();
            //     // }
            //     //if(!aSource.isPlaying){aSource.Play();}
            //     aSource.timeSamples = masterSpeaker.timeSamples;
            //     //reverseLoopOrder = true;
            // }
        }
    }

    void InputDropdownValueChanged(Dropdown change)
    {
        if (!isAppListRefreshing)
        {
            var newInput = AudioInputDropdown.options[AudioInputDropdown.value].text;
            var audioCableOutput = audioEndpointsJson.endpoints.FirstOrDefault(e => e.name == newInput);

            if (audioCableOutput == null)
            {
                AppSelectionDropdown.interactable = false;
                var session = audioEndpointsJson.endpoints
                .SelectMany(e => e.sessions)
                .Where(s => s.exeName == appExeName)
                .FirstOrDefault();
                if (session != null)
                {
                    StartCoroutine(resetAppToOriginalEndpoint(session.processId));
                }
            }
            else
            {
                AppSelectionDropdown.interactable = true;
                if (AppSelectionDropdown.value != 0)
                {
                    var session = audioEndpointsJson.endpoints
                    .SelectMany(e => e.sessions)
                    .Where(s => s.exeName == appExeName)
                    .FirstOrDefault();
                    if (session != null)
                    {
                        StartCoroutine(setAppToVAC(session.processId, newInput, false));
                    }
                }
            }
            Microphone.End(inputName);
            PlayerPrefs.SetString("InputName", newInput);
            PlayerPrefs.Save();
            inputName = newInput;
            sourceInit();
        }
    }

    void ReverbPresetDropdownValueChanged(Dropdown change)
    {
        var newPreset = ReverbPresetDropdown.options[ReverbPresetDropdown.value].text;
        AudioReverbPreset newPresetEnum;

        if (Enum.TryParse<AudioReverbPreset>(newPreset, out newPresetEnum))
        {
            if(newPresetEnum != globalReverbPreset && isReady){
                PlayerPrefs.SetInt("GlobalReverbPreset", (int)newPresetEnum);
                PlayerPrefs.Save();
                globalReverbPreset = newPresetEnum;
                if(!inSpawnRoom && !goalHornPlaying){
                    foreach (AudioReverbFilter reverb in speakerReverbs.Values)
                    {
                        reverb.reverbPreset = globalReverbPreset;
                    }
                }
            }
        }
    }

    void SpawnRoomReverbPresetDropdownValueChanged(Dropdown change)
    {
        var newPreset = SpawnRoomReverbPresetDropdown.options[SpawnRoomReverbPresetDropdown.value].text;
        AudioReverbPreset newPresetEnum;

        if (Enum.TryParse<AudioReverbPreset>(newPreset, out newPresetEnum))
        {
            if(newPresetEnum != spawnRoomReverbPreset && isReady){
                PlayerPrefs.SetInt("SpawnRoomReverbPreset", (int)newPresetEnum);
                PlayerPrefs.Save();
                spawnRoomReverbPreset = newPresetEnum;
                if(inSpawnRoom && !goalHornPlaying){
                    foreach (AudioReverbFilter reverb in speakerReverbs.Values)
                    {
                        reverb.reverbPreset = spawnRoomReverbPreset;
                    }
                }
            }
        }
    }

    public void GoalHornVolMultChanged(float sliderValue) {
         goalHornVolumeUserMult = (0.5f +(sliderValue * 0.05f));
         PlayerPrefs.SetFloat("GoalHornVolumeUserMult", goalHornVolumeUserMult);
         PlayerPrefs.Save();
    }

    public void GoalHornTimeChanged(float sliderValue) {
        goalHornMaxDuration = sliderValue;
        PlayerPrefs.SetFloat("GoalHornMaxDuration", goalHornMaxDuration);
        PlayerPrefs.Save();
    }

    public void SpawnRoomVolumeFloorChanged(float sliderValue) {
        spawnRoomVolumeFloor = ((sliderValue * 0.05f));
        PlayerPrefs.SetFloat("SpawnRoomVolumeFloor", spawnRoomVolumeFloor);
        PlayerPrefs.Save();
    }
    public void SpawnRoomLowPassFloorChanged(float sliderValue) {
        spawnRoomLowPassFloor = ((sliderValue * 200f));
        if(spawnRoomLowPassFloor > 0f){
            playerListernerLowPass.enabled = true;
        }else{
            playerListernerLowPass.enabled = false;
        }
        PlayerPrefs.SetFloat("spawnRoomLowPassFloor", spawnRoomLowPassFloor);
        PlayerPrefs.Save();
    }
    void AppSelectionDropdownValueChanged(Dropdown change)
    {
        var newAppSource = AppSelectionDropdown.options[AppSelectionDropdown.value].text;
        if (isFirstAppInit || newAppSource != appExeName)
        {
            isFirstAppInit = false;
            var oldSession = audioEndpointsJson.endpoints
                .SelectMany(e => e.sessions)
                .Where(s => s.exeName == appExeName)
                .FirstOrDefault();
            if (oldSession != null)
            {
                AppEndpoint originalEndpoint = originalAppEndpoints.FirstOrDefault(appEP => appEP.processId == oldSession.processId);
                if (originalEndpoint != null)
                {
                    StartCoroutine(resetAppToOriginalEndpoint(oldSession.processId));
                }
            }
            if (AppSelectionDropdown.value == 0)
            {
                appExeName = "";
                PlayerPrefs.SetString("AppSourceName", appExeName);
                PlayerPrefs.Save();
            }
            else
            {
                PlayerPrefs.SetString("AppSourceName", newAppSource);
                PlayerPrefs.Save();
                appExeName = newAppSource;
                var newSession = audioEndpointsJson.endpoints
                .SelectMany(e => e.sessions)
                .Where(s => s.exeName == appExeName)
                .FirstOrDefault();
                if (newSession != null)
                {
                    StartCoroutine(setAppToVAC(newSession.processId, inputName));
                    Microphone.End(inputName);
                    sourceInit();
                }
            }
        }
    }

    void OpenWinAppAudioSettings()
    {
        Application.OpenURL("ms-settings:apps-volume");
    }
    void DownloadLatestRelease()
    {
        try
        {
            updateFileName = "EchoSpeakerSystemInstall_" + latestReleaseVer + ".exe";
            WebClient webClient = new WebClient();
            webClient.DownloadFileCompleted += Completed;
            webClient.DownloadFileAsync(new Uri(latestReleaseURL), Path.GetTempPath() + updateFileName);
        }
        catch
        {

        }
        UpdateDownloadBtnGameObject.SetActive(false);
    }
    private void Completed(object sender, AsyncCompletedEventArgs e)
    {

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Path.GetTempPath(), updateFileName),
            UseShellExecute = true
        });

        Application.Quit();
    }
    void OpenCableDownload()
    {
        Application.OpenURL(RecommendedCable.downloadUrl);
        VACDownloadBtnGameObject.SetActive(false);
    }
    private IEnumerator SyncSourcesInit()
    {
        //speakerEchos[masterSpeaker.name].delay = 0f;
        yield return new WaitForSeconds(2);
        masterSpeaker.clip = null;
        masterSpeaker.clip = masterClip;
        foreach (AudioSource aSource in speakers)
        {
            //speakerEchos[aSource.name].delay = 0f;
            aSource.clip = null;
            aSource.clip = masterClip;
            aSource.timeSamples = masterSpeaker.timeSamples;
            aSource.mute = false;
        }
        yield return null;
        foreach (AudioReverbFilter reverb in speakerReverbs.Values)
        {
            reverb.reverbPreset = globalReverbPreset;
        }
        loops = 0;
        isReady = true;
        playerListener.speakersReady = true;
        yield return new WaitForSeconds(0.5f);
        StartCoroutine(StartFade(4, 1));
    }

    private IEnumerator SyncSourcesAfterGoal()
    {
        //speakerEchos[masterSpeaker.name].delay = 0f;
        float waitTime = goalHornMaxDuration;
        if (goalHornClip.length < waitTime)
        {
            waitTime = goalHornClip.length - 2.55f;
        }
        yield return new WaitForSeconds(waitTime);
        StartCoroutine(StartFade(2.5f, 0.001f));
        yield return new WaitForSeconds(2.48f);
        // float currentTime = 0;
        // float currentVol = listenerVolume;
        // float targetValue = Mathf.Clamp(0.3f, 0.0001f, 1);

        // while (currentTime < 3f)
        // {
        //     currentTime += Time.deltaTime;
        //     float newVol = Mathf.Lerp(currentVol, targetValue, currentTime / 3f);
        //     listenerVolume = newVol;
        //     UnityEngine.Debug.Log(listenerVolume);
        //     yield return null;
        // }
        // yield break;
        // masterSpeaker.volume *= 2f;
        // masterSpeaker.timeSamples = lastPos;
        // masterSpeaker.clip = masterClip;
        // masterSpeaker.loop = true;
        StartCoroutine(StartFade(0.15f, 0));

        foreach (AudioSource aSource in speakers)
        {
            if(!inSpawnRoom){
                speakerReverbs[aSource.name].reverbPreset = globalReverbPreset;
            }
            speakerEchos[aSource.name].delay = 0f;
            aSource.dopplerLevel = 0.0f;
            //aSource.clip = null;
            aSource.loop = true;
            aSource.clip = masterClip;
            aSource.timeSamples = masterSpeaker.timeSamples;
            aSource.volume *= 1f / goalHornClipVolMult;
        }
        goalHornPlaying = false;
        loops = 0;
        isReady = true;
        goalHornToggle.enabled = true;
        goalHornVolMultSlider.enabled = true;
        spawnRoomVolFloorSlider.enabled = true;
        goalHornTimeSlider.enabled = true;
        playerListener.speakersReady = true;
        yield return null;
        // foreach (AudioSource aSource in speakers)
        // {
        //     // aSource.clip = masterClip;
        //     aSource.loop = true;
        //     //aSource.timeSamples = masterSpeaker.timeSamples;
        //     aSource.volume *= 3f;
        // }
        StartCoroutine(StartFade(2.5f, 1));
        // StartCoroutine(SyncSources());
    }
    private IEnumerator ResetAudio()
    {
        //  while (true)
        isReady = false;
        playerListener.speakersReady = false;
        Microphone.End(inputName);
        //PlayerPrefs.SetString("InputName", newInput);
        //PlayerPrefs.Save();
        //inputName = newInput;
        sourceInit();
        yield return null;
        // //  {
        //      foreach (AudioSource aSource in speakers)
        //      {
        //          aSource.timeSamples = masterSpeaker.timeSamples;
        //          yield return null;
        //      }
        //      loops = 0;
        //  }
    }
    private IEnumerator SyncSources()
    {
        bool shouldSetVol = listenerVolume != 0.0f;
        StartCoroutine(StartFade(0.15f, 0));
        //  while (true)
        //  {
        //previousTimeSamples = masterSpeaker.timeSamples;
        //masterSpeaker.clip = null;
        // speakerEchos[masterSpeaker.name].delay = 0f;
        //masterSpeaker.clip = masterClip;
        //masterSpeaker.timeSamples = previousTimeSamples;
        foreach (AudioSource aSource in speakers)
        {
            speakerEchos[aSource.name].delay = 0f;
            aSource.dopplerLevel = 0.0f;
            //aSource.clip = null;
            aSource.clip = masterClip;
            aSource.timeSamples = masterSpeaker.timeSamples;
        }
        if(shouldSetVol){
            // yield return new WaitForSeconds(0.16f);
            StartCoroutine(StartFade(0.15f, 1));
        }
        loops = 0;
        isReady = true;
        playerListener.speakersReady = true;
        yield return null;
        //  }
    }

    private IEnumerator SyncSourcesGoalHorn()
    {
        goalHornToggle.enabled = false;
        goalHornVolMultSlider.enabled = false;
        goalHornTimeSlider.enabled = false;
        StartCoroutine(StartFade(0.15f, 0));

        //  while (true)
        //  {
        //masterSpeaker.clip = null;
        // speakerEchos[masterSpeaker.name].delay = 0f;
        // masterSpeaker.clip = goalHornClip;
        // masterSpeaker.timeSamples = 0;
        // foreach (AudioReverbFilter reverb in speakerReverbs.Values)
        // {
        //     reverb.reverbPreset = AudioReverbPreset.Generic;
        // }
        foreach (AudioSource aSource in speakers)
        {
            speakerReverbs[aSource.name].reverbPreset = AudioReverbPreset.Generic;
            speakerEchos[aSource.name].delay = 0f;
            // aSource.Stop();
            aSource.dopplerLevel = 0f;
            aSource.clip = goalHornClip;
            aSource.timeSamples = 13000;
        }
        StartCoroutine(StartFade(0.15f, 1));
        // foreach (AudioSource aSource in speakers)
        // {
        //     aSource.Play();                        
        // }

        loops = 0;
        isReady = true;
        playerListener.speakersReady = true;
        yield return null;
        //  }
    }

    public IEnumerator StartFade(float duration, float targetVolume)
    {
        float currentTime = 0;
        float currentVol = listenerVolume;
        float targetValue = Mathf.Clamp(targetVolume, 0.0001f, 1);

        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;
            float newVol = Mathf.Lerp(currentVol, targetValue, currentTime / duration);
            listenerVolume = newVol;
            //UnityEngine.Debug.Log(listenerVolume);
            yield return null;
        }
        yield break;
    }

    private IEnumerator setAppToVAC(int procID, string input, bool retainOriginalEndpoint = true)
    {
        var audioCableOutput = audioEndpointsJson.endpoints.FirstOrDefault(e => e.name == input);
        if (audioCableOutput != null)
        {
            Process AudioSwitch = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = (Application.streamingAssetsPath + "\\AudioSwitch.exe"),
                    Arguments = procID + " \"" + audioCableOutput.id + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }

            };
            AudioSwitch.Start();
            AudioSwitch.WaitForExit(900);
            string line = AudioSwitch.StandardOutput.ReadLine();
            if (AudioSwitch.ExitCode == 0)
            {
                if (retainOriginalEndpoint)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        originalAppEndpoints.Add(new AppEndpoint {appName = appExeName, processId = procID, originalEndpointID = "" });
                    }
                    else
                    {
                        originalAppEndpoints.Add(new AppEndpoint {appName = appExeName, processId = procID, originalEndpointID = line });
                    }
                    SaveOriginalAppEndpoints();
                }
            }
        }
        StartCoroutine(SyncSources());
        yield return null;
    }
    private IEnumerator resetAppToOriginalEndpoint(int procID)
    {
        AppEndpoint originalEndpoint = originalAppEndpoints.FirstOrDefault(appEP => appEP.processId == procID);
        if (originalEndpoint != null)
        {
            Process AudioSwitch = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = (Application.streamingAssetsPath + "\\AudioSwitch.exe"),
                    Arguments = procID + " \"" + originalEndpoint.originalEndpointID + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }

            };
            AudioSwitch.Start();
            AudioSwitch.WaitForExit(900);
            originalAppEndpoints.Remove(originalEndpoint);
            SaveOriginalAppEndpoints();
        }
        yield return null;
    }

    void OnApplicationQuit()
    {
        try
        {
            Cleanup();

        }
        catch (Exception)
        {
            UnityEngine.Debug.Log("Disconnect failed");
        }
    }

    void Cleanup()
    {
        if (hasCleanedUp)
        {
            return;
        }
        // Marked up front: Cleanup is reachable from both FixedUpdate and
        // OnApplicationQuit, and every step below must run at most once. Each step is
        // guarded separately so one failure cannot skip the others - restoring the user's
        // audio endpoints matters even if the socket teardown throws.
        hasCleanedUp = true;

        // Never leave the user's music paused because ESS exited.
        try { ResumePausedMedia(); } catch (Exception) { }
        try { playerListener.Cleanup(); } catch (Exception) { }
        try { Microphone.End(inputName); } catch (Exception) { }
        try { RestoreAllAppEndpoints(); } catch (Exception) { }
    }

    /// <summary>
    /// Points every application we redirected back at the endpoint it was on before we
    /// touched it, then clears the crash-recovery record. The old code only ever restored
    /// the currently selected app, and sent it to the default device rather than to the
    /// endpoint actually recorded for it.
    /// </summary>
    void RestoreAllAppEndpoints()
    {
        if (originalAppEndpoints.Count == 0)
        {
            return;
        }
        // Endpoints are re-applied by exe name, so refresh the session list first in case
        // a process id moved while we were running.
        try { audioEndpointsJson = GetAudioInfo(); } catch (Exception) { }

        foreach (AppEndpoint endpoint in originalAppEndpoints.ToArray())
        {
            try
            {
                ResetAppEndpoint(endpoint.appName, endpoint.originalEndpointID);
            }
            catch (Exception) { }
        }
        originalAppEndpoints.Clear();
        SaveOriginalAppEndpoints();
    }

    void ResetAppEndpointFromPreviousSession(string appName, string endpoint){
        ResetAppEndpoint(appName, endpoint);
    }
    void ResetAppEndpoint(string appName, string endpoint){
         var Originalsession = audioEndpointsJson.endpoints
                .SelectMany(e => e.sessions)
                .Where(s => s.exeName == appName)
                .FirstOrDefault();
                    if (Originalsession != null)
                    {
                        Process AudioSwitch = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = (Application.streamingAssetsPath + "\\AudioSwitch.exe"),
                                Arguments = Originalsession.processId + " \"" + endpoint + "\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardInput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }

                        };
                        AudioSwitch.Start();
                        AudioSwitch.WaitForExit(900);
                    }
    }
    IEnumerator GetLatestVer()
    {
        using (UnityWebRequest webRequest = UnityWebRequest.Get(
            "https://api.github.com/repos/heisthecat31/Echo-VR-Speaker-System/releases/latest"))
        {
            yield return webRequest.SendWebRequest();
            // A repo with no published releases answers 404, which sets isHttpError rather
            // than isNetworkError. Without this the error body was parsed as a release.
            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                yield break;
            }
            try
            {
                VersionJson latestVersion =
                    JsonUtility.FromJson<VersionJson>(webRequest.downloadHandler.text);
                if (latestVersion == null || string.IsNullOrEmpty(latestVersion.tag_name)
                    || latestVersion.assets == null)
                {
                    yield break;
                }
                if (!IsNewerVersion(latestVersion.tag_name, VERSION_TAGNAME))
                {
                    yield break;
                }
                Asset installer = latestVersion.assets
                    .FirstOrDefault(a => a.browser_download_url != null
                                         && a.browser_download_url.EndsWith("exe"));
                if (installer == null)
                {
                    yield break;   // release published without an installer attached
                }
                latestReleaseVer = latestVersion.tag_name;
                latestReleaseURL = installer.browser_download_url;
                UpdateDownloadBtn.onClick.RemoveAllListeners();
                UpdateDownloadBtn.onClick.AddListener(delegate
                {
                    DownloadLatestRelease();
                });
                UpdateDownloadBtnGameObject.SetActive(true);
            }
            catch (Exception)
            {
            }
        }
    }

    IEnumerator GetWhatsNew()
    {
        string body = null;
        using (UnityWebRequest webRequest = UnityWebRequest.Get(
            "https://api.github.com/repos/heisthecat31/Echo-VR-Speaker-System/releases"))
        {
            yield return webRequest.SendWebRequest();
            if (!webRequest.isNetworkError && !webRequest.isHttpError)
            {
                try
                {
                    VersionJson[] releases = JsonHelper.FromJson<VersionJson>(webRequest.downloadHandler.text);
                    VersionJson thisRelease = releases.FirstOrDefault(r => r.tag_name == VERSION_TAGNAME);
                    if (thisRelease != null)
                    {
                        body = thisRelease.body;
                    }
                }
                catch (Exception) { }
            }
        }
        ShowWhatsNew(body);
    }

    void PauseWhenNotInGameChanged(Toggle change)
    {
        pauseWhenNotInGame = change.isOn;
        PlayerPrefs.SetInt("PauseWhenNotInGame", pauseWhenNotInGame ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Optional: ask Windows to pause whatever is playing while Echo VR is not reporting
    /// an active match, and resume it when a match comes back. This drives the real media
    /// session (the same thing the volume overlay controls) through MediaControl.exe,
    /// because Unity's Mono runtime cannot reach WinRT itself.
    /// </summary>
    void UpdateGameActivityPause()
    {
        if (!pauseWhenNotInGame)
        {
            if (musicPausedForGame)
            {
                musicPausedForGame = false;
                ResumePausedMedia();
            }
            return;
        }
        bool inGame = playerListener.IsInGame;
        if (!inGame && !musicPausedForGame)
        {
            musicPausedForGame = true;
            PauseActiveMedia();
        }
        else if (inGame && musicPausedForGame)
        {
            musicPausedForGame = false;
            ResumePausedMedia();
        }
    }

    /// <summary>
    /// Runs MediaControl.exe and returns whatever it wrote to stdout, or null if the
    /// helper is missing or Windows has no media session API (pre-1809).
    /// </summary>
    string RunMediaControl(string arguments)
    {
        try
        {
            string exe = Application.streamingAssetsPath + "\\MediaControl.exe";
            if (!File.Exists(exe))
            {
                return null;
            }
            Process helper = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            helper.Start();
            // Read before waiting: a full pipe buffer would otherwise deadlock the wait.
            string output = helper.StandardOutput.ReadToEnd();
            helper.WaitForExit(3000);
            return output;
        }
        catch (Exception)
        {
            return null;
        }
    }

    void PauseActiveMedia()
    {
        pausedMediaApps.Clear();
        string output = RunMediaControl("pause");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }
        foreach (string line in output.Split('\n'))
        {
            string id = line.Trim();
            if (id.Length > 0)
            {
                pausedMediaApps.Add(id);
            }
        }
    }

    void ResumePausedMedia()
    {
        if (pausedMediaApps.Count == 0)
        {
            return;
        }
        StringBuilder args = new StringBuilder("play");
        foreach (string id in pausedMediaApps)
        {
            args.Append(" \"").Append(id).Append("\"");
        }
        RunMediaControl(args.ToString());
        pausedMediaApps.Clear();
    }

    /// <summary>
    /// Puts the app you last used back on the virtual cable as soon as ESS starts, so music
    /// routes through the speakers without having to re-pick it from the dropdown.
    /// </summary>
    void AutoSwitchRememberedAppToCable()
    {
        if (string.IsNullOrEmpty(appExeName) || !IsVirtualCableDevice(inputName))
        {
            return;
        }
        if (audioEndpointsJson == null || audioEndpointsJson.endpoints == null)
        {
            return;
        }
        Session session = audioEndpointsJson.endpoints
            .SelectMany(e => e.sessions)
            .FirstOrDefault(sn => sn.exeName == appExeName);
        if (session == null)
        {
            return;
        }
        // Never switch twice: a second pass would record the cable itself as the app's
        // "original" endpoint and we would restore it to the cable on exit.
        if (originalAppEndpoints.Any(ep => ep.processId == session.processId))
        {
            return;
        }
        Endpoint cable = audioEndpointsJson.endpoints.FirstOrDefault(e => e.name == inputName);
        if (cable != null && cable.sessions != null
            && cable.sessions.Any(sn => sn.processId == session.processId))
        {
            return;   // already routed to the cable
        }
        isFirstAppInit = false;
        StartCoroutine(setAppToVAC(session.processId, inputName));
    }

    /// <summary>
    /// True when a release tag is genuinely newer than what we are running. The old check
    /// was a plain inequality, which offered a "update" whenever the local build was ahead
    /// of the newest published release.
    /// </summary>
    static bool IsNewerVersion(string remoteTag, string localTag)
    {
        int[] remote = ParseVersion(remoteTag);
        int[] local = ParseVersion(localTag);
        if (remote == null || local == null)
        {
            return remoteTag != localTag;
        }
        for (int i = 0; i < 3; i++)
        {
            if (remote[i] != local[i])
            {
                return remote[i] > local[i];
            }
        }
        return false;
    }

    static int[] ParseVersion(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return null;
        }
        string[] parts = tag.TrimStart('v', 'V').Split('.');
        int[] nums = new int[3];
        for (int i = 0; i < 3 && i < parts.Length; i++)
        {
            string digits = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out nums[i]))
            {
                return null;
            }
        }
        return nums;
    }

    /// <summary>
    /// Renders the release notes, falling back to the notes compiled into this build when
    /// GitHub has no release for this tag or could not be reached.
    /// </summary>
    void ShowWhatsNew(string body)
    {
        body = string.IsNullOrEmpty(body)
            ? LOCAL_WHATS_NEW
            : body.Replace("**", "").Replace(" _", " ").Replace("_ ", " ");

        Transform canvas = GameObject.Find("UICanvas").transform;
        Transform title = canvas.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "WhatsNewTitle");
        Transform text = canvas.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "WhatsNewText");
        Transform popup = canvas.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "WhatsNewPopup");

        if (title != null)
        {
            title.GetComponent<Text>().text = "What's New (" + VERSION_TAGNAME + ")";
        }
        if (text != null)
        {
            text.GetComponent<Text>().text = body;
        }
        if (popup != null)
        {
            popup.gameObject.SetActive(isNewUpdate);
        }
        if (isNewUpdate)
        {
            PlayerPrefs.SetString("RunningVersion", VERSION_TAGNAME);
            PlayerPrefs.Save();
        }
    }

    float Map(float s, float a1, float a2, float b1, float b2)
    {
        return b1 + (s - a1) * (b2 - b1) / (a2 - a1);
    }
}

/// <summary>
/// A virtual audio cable product Echo Speaker System can capture from.
/// </summary>
public class VirtualCable
{
    public readonly string displayName;
    public readonly string downloadUrl;
    public readonly string playbackDeviceHint;
    public readonly string[] deviceNameTokens;

    public VirtualCable(string displayName, string downloadUrl, string playbackDeviceHint,
        string[] deviceNameTokens)
    {
        this.displayName = displayName;
        this.downloadUrl = downloadUrl;
        this.playbackDeviceHint = playbackDeviceHint;
        this.deviceNameTokens = deviceNameTokens;
    }
}

public static class JsonHelper
    {
        public static T[] FromJson<T>(string jsonArray)
        {
            jsonArray = WrapArray (jsonArray);
            return FromJsonWrapped<T> (jsonArray);
        }
 
        public static T[] FromJsonWrapped<T> (string jsonObject)
        {
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(jsonObject);
            return wrapper.items;
        }
 
        private static string WrapArray (string jsonArray)
        {
            return "{ \"items\": " + jsonArray + "}";
        }
 
        public static string ToJson<T>(T[] array)
        {
            Wrapper<T> wrapper = new Wrapper<T>();
            wrapper.items = array;
            return JsonUtility.ToJson(wrapper);
        }
 
        public static string ToJson<T>(T[] array, bool prettyPrint)
        {
            Wrapper<T> wrapper = new Wrapper<T>();
            wrapper.items = array;
            return JsonUtility.ToJson(wrapper, prettyPrint);
        }
 
        [Serializable]
        private class Wrapper<T>
        {
            public T[] items;
        }
    }
[System.Serializable]
public class VersionJson
{
    public string tag_name;
    public Author author;
    public string html_url;
    public Asset[] assets;
    public string body;
}
[System.Serializable]
public class Asset
{
    public string browser_download_url;
    public Uploader uploader;
}
[System.Serializable]
public class Author
{
    public string html_url;
}
[System.Serializable]
public class Uploader
{
    public string html_url;
}

//Serializable classes for JSON serializing from the API output.
[System.Serializable]
public class Game
{
    public bool isNewstyle;
    public float caprate;
    public long nframes;
    public Frame[] frames;
}
[System.Serializable]
public class Stats
{
    public int possession_time;
    public int points;
    public int goals;
    public int saves;
    public int stuns;
    public int interceptions;
    public int blocks;
    public int passes;
    public int catches;
    public int steals;
    public int assists;
    public int shots_taken;
}
[System.Serializable]
public class Last_Score
{
    public float disc_speed;
    public string team;
    public string goal_type;
    public int point_amount;
    public float distance_thrown;
    public string person_scored;
    public string assist_scored;
}
[System.Serializable]
public class Frame
{
    public Disc disc;
    public double frameTimeOffset;

    public string sessionid;
    public int orange_points;
    public bool private_match;
    public string client_name;
    public string game_clock_display;
    public string game_status;
    public float game_clock;
    public string match_type;

    public Team[] teams;

    public string map_name;
    public int[] possession;
    public bool tournament_match;
    public int blue_points;

    public Last_Score last_score;


}
[System.Serializable]
public class Disc
{
    public float[] position;
    public float[] velocity;
    public int bounce_count;
}
[System.Serializable]
public class Team
{
    public Player[] players;
    public string team;
    public bool possession;
    public Stats stats;

}
[System.Serializable]
public class Player
{
    public string name;
    // public float[] rhand;
    public int playerid;
    public Head head;
    public Body body;
    public Lhand lhand;
    public Rhand rhand;
    public float[] position;
    // public float[] lhand;
    public long userid;
    public Stats stats;
    public int number;
    public int level;
    public bool possession;
    // public float[] left;
    public bool invulnerable;
    // public float[] up;
    // public float[] forward;
    public bool stunned;
    public float[] velocity;
    public bool blocking;
}
[System.Serializable]
public class Head
{
    public float[] position;
    public float[] left;
    public float[] up;
    public float[] forward;
}
[System.Serializable]
public class Body
{
    public float[] position;
    public float[] left;
    public float[] up;
    public float[] forward;
}
[System.Serializable]
public class Lhand
{
    public float[] pos;
    public float[] left;
    public float[] up;
    public float[] forward;
}
[System.Serializable]
public class Rhand
{
    public float[] pos;
    public float[] left;
    public float[] up;
    public float[] forward;
}

[System.Serializable]
public class MatchEvent
{
    public string EventTypeName;
    public EventData[] Data;
}

[System.Serializable]
public class EventData
{
    public string Key;
    public string Value;
}

[System.Serializable]
public class AudioEndpoints
{
    public Endpoint[] endpoints;
}
[System.Serializable]
public class Endpoint
{
    public Session[] sessions;
    public string name;
    public string id;
}
[System.Serializable]
public class Session
{
    public string exeName;
    public int processId;
}

[System.Serializable]
public class AppAudioEndpoints
{
    public List<AppEndpoint> endpoints;
}
[System.Serializable]
public class AppEndpoint
{
    public string appName;
    public string originalEndpointID;
    public int processId;
}

public class PlayerStats : Stats
{
    public PlayerStats(int pt, int points, int goals, int saves, int stuns, int interceptions, int blocks, int passes, int catches, int steals, int assists, int shots_taken)
    {
        this.possession_time = pt;
        this.points = points;
        this.goals = goals;
        this.saves = saves;
        this.stuns = stuns;
        this.interceptions = interceptions;
        this.blocks = blocks;
        this.passes = passes;
        this.catches = catches;
        this.steals = steals;
        this.assists = assists;
        this.shots_taken = shots_taken;
    }
}