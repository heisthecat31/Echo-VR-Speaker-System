using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Networking;
using NetMQ;
using NetMQ.Sockets;
using System.Threading;

public class SpatialPlayerListener : MonoBehaviour
{
    GameObject playerObject;
    static string echoVRIP = "127.0.0.1";
    static string echoVRPort = "6721";
    static string url = "http://" + echoVRIP + ":" + echoVRPort + "/session";
    public Vector3 headForward;
    public bool goalScored = false;
    public Vector3 headUp;
    public Transform head;
    public bool isIgniteBotEmbedded = false;
    public bool isReverbMixChangeOn = true;
    public NetMQPoller poller;
    public SubscriberSocket subSocket;
    public string addr = "tcp://localhost:12345";
    [Tooltip("Seconds for the listener to close most of the gap to a new head pose. "
        + "Lower is snappier, higher is smoother.")]
    public float headSmoothingTime = 0.05f;
    const float API_RETRY_DELAY_SECONDS = 1f;
    // How long without a parsed frame before we call the player "not in game".
    const float GAME_DATA_TIMEOUT_SECONDS = 2f;

    float[] _playerHeadPosition;
    float[] _playerHeadForward;
    float[] _playerHeadUp;

    string tempPlayerName = "";
    bool isClientSpectator = false;
    bool lastRequestFailed = false;
    // Set on whichever thread parsed a frame (the NetMQ poller has no access to
    // Unity's time API), then consumed on the main thread in Update().
    volatile bool frameArrived = false;
    float lastFrameRealtime = -999f;
    public bool quitCalled = false;
    public bool hasCleanedUp = false;
    public bool speakersReady = false;
    // Start is called before the first frame update
    public void Start()
    {
        SetDefaultListenerPosition();
        string[] args = System.Environment.GetCommandLineArgs();
        // Start at 1: args[0] is the executable path, so an install directory
        // containing "ignitebot" would otherwise force Spark-embedded mode.
        for (int i = 1; i < args.Length; i++)
        {
            Debug.Log("ARG " + i + ": " + args[i]);
            if (args[i].Contains("ignitebot"))
            {
                isIgniteBotEmbedded = true;
                break;
            }
        }
        playerObject = GameObject.Find("Player Listener");
        if (playerObject == null)
        {
            playerObject = gameObject;
        }
        if (isIgniteBotEmbedded)
        {
            AsyncIO.ForceDotNet.Force();
            NetMQConfig.Cleanup();
            poller = new NetMQPoller();
            subSocket = new SubscriberSocket();

            subSocket.ReceiveReady += OnReceiveReady;
            subSocket.Options.ReceiveHighWatermark = 10;
            subSocket.Connect(addr);
            subSocket.Subscribe("RawFrame");
            subSocket.Subscribe("CloseApp");
            subSocket.Subscribe("MatchEvent");

            poller.Add(subSocket);
            poller.RunAsync();
        }
        else
        {
            // One request in flight at a time. Update() used to kick off a fresh
            // UnityWebRequest every frame - roughly 60 a second - so requests piled up
            // against the Echo VR API faster than it could answer them.
            StartCoroutine(PollApiLoop());
        }
    }

    IEnumerator PollApiLoop()
    {
        while (!quitCalled)
        {
            if (!speakersReady)
            {
                yield return null;
                continue;
            }
            yield return GetRequest();
            if (lastRequestFailed)
            {
                // Echo VR is not running, or its API is switched off. Back off instead
                // of hammering a refused connection every frame.
                yield return new WaitForSeconds(API_RETRY_DELAY_SECONDS);
            }
        }
    }

    /// <summary>
    /// True while Echo VR is still delivering match frames. Goes false a couple of
    /// seconds after the game closes, the API is switched off, or a match is left.
    /// </summary>
    public bool IsInGame
    {
        get { return Time.realtimeSinceStartup - lastFrameRealtime < GAME_DATA_TIMEOUT_SECONDS; }
    }

    /// <summary>
    /// Pulls the local player's head transform (or, when spectating, the followed
    /// player's) out of an API frame. Shared by the Spark/NetMQ and standalone HTTP paths,
    /// which previously carried two copies of this logic.
    /// </summary>
    void ApplyFrame(Frame apiFrame)
    {
        if (apiFrame == null || apiFrame.teams == null)
        {
            return;
        }
        bool found = false;
        foreach (Team t in apiFrame.teams)
        {
            if (t == null || t.players == null)
            {
                continue;
            }
            foreach (Player p in t.players)
            {
                if (p == null)
                {
                    continue;
                }
                if (p.name == apiFrame.client_name)
                {
                    if (t.team == "SPECTATORS")
                    {
                        isClientSpectator = true;
                    }
                    else
                    {
                        found = true;
                        isClientSpectator = false;
                        SetHead(p.head);
                    }
                }
                else if (isClientSpectator)
                {
                    if (tempPlayerName.Length < 1)
                    {
                        if (t.team != "SPECTATORS" && !found)
                        {
                            found = true;
                            tempPlayerName = p.name;
                            SetHead(p.head);
                        }
                    }
                    else if (p.name == tempPlayerName && t.team != "SPECTATORS")
                    {
                        found = true;
                        tempPlayerName = p.name;
                        SetHead(p.head);
                    }
                }
            }
        }
        if (!found && tempPlayerName.Length > 0)
        {
            tempPlayerName = "";
        }
        frameArrived = true;
    }

    /// <summary>
    /// Only publishes a pose once every component is present and long enough to read, so
    /// Update() can never index past the end of a short array from a malformed frame.
    /// </summary>
    void SetHead(Head head)
    {
        if (head == null
            || head.position == null || head.position.Length < 3
            || head.forward == null || head.forward.Length < 3
            || head.up == null || head.up.Length < 3)
        {
            return;
        }
        _playerHeadPosition = head.position;
        _playerHeadForward = head.forward;
        _playerHeadUp = head.up;
    }

    public void Cleanup()
    {
        if (isIgniteBotEmbedded && !hasCleanedUp)
        {
            poller.StopAsync();
            Thread.Sleep(10);
            subSocket.Dispose();
            poller.Dispose();
            NetMQConfig.Cleanup(false);
            hasCleanedUp = true;
        }
    }

    void OnReceiveReady(object sender, NetMQSocketEventArgs e)
    {
        if (quitCalled)
        {
            return;
        }
        var str = e.Socket.ReceiveFrameString();
        if (str == "CloseApp")
        {
            quitCalled = true;
        }
        else if (str == "MatchEvent")
        {
            string messageReceived = e.Socket.ReceiveFrameString();
            try
            {
                MatchEvent eventMSG = JsonUtility.FromJson<MatchEvent>(messageReceived);
                if (eventMSG.EventTypeName == "LeaveMatch")
                {
                    SetDefaultListenerPosition();
                }
                else if (eventMSG.EventTypeName == "GoalScored")
                {
                    if (eventMSG.Data[0].Value == "True")
                    {
                        goalScored = true;
                    }
                }
            }
            catch (Exception) { }
        }
        else if (str == "RawFrame")
        {
            string messageReceived = e.Socket.ReceiveFrameString();
            // No Thread.Sleep here: this runs on the NetMQ poller thread, and sleeping in
            // the handler stalled every other subscription (including CloseApp) while the
            // receive queue backed up against a high-water mark of 10.
            try
            {
                ApplyFrame(JsonUtility.FromJson<Frame>(messageReceived));
            }
            catch (Exception)
            {
                SetDefaultListenerPosition();
            }
        }
    }

    void Update()
    {
        if (quitCalled)
        {
            Cleanup();
            return;
        }
        if (frameArrived)
        {
            frameArrived = false;
            lastFrameRealtime = Time.realtimeSinceStartup;
        }
        if (!speakersReady)
        {
            return;
        }

        // Snapshot the shared arrays once. They are swapped wholesale by the NetMQ poller
        // thread, so re-reading the fields mid-frame could mix two different frames.
        float[] position = _playerHeadPosition;
        float[] forward = _playerHeadForward;
        float[] up = _playerHeadUp;
        if (position == null || position.Length < 3
            || forward == null || forward.Length < 3
            || up == null || up.Length < 3)
        {
            return;
        }

        // This used to start a fresh 0.05s coroutine every frame, so three of them
        // overlapped at 60fps and the oldest one snapped the transform back to a stale
        // target as it finished. A framerate-independent smooth does the same job with no
        // allocation and nothing fighting over the transform.
        float t = headSmoothingTime <= 0f
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / headSmoothingTime);

        Vector3 targetPosition = new Vector3(position[2], position[1], position[0]);
        playerObject.transform.position =
            Vector3.Lerp(playerObject.transform.position, targetPosition, t);

        headUp = new Vector3(up[2], up[1], up[0]);
        headForward = new Vector3(forward[2], forward[1], forward[0]);
        if (headForward.sqrMagnitude > 0f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(headForward, headUp);
            playerObject.transform.rotation =
                Quaternion.Slerp(playerObject.transform.rotation, targetRotation, t);
        }
    }

    void SetDefaultListenerPosition(){
        // Literal values rather than re-parsing a JSON document every time the API drops
        // out; these match the defaultPos string this used to deserialise.
        _playerHeadPosition = new float[] { 0f, 0f, -23f };
        _playerHeadForward = new float[] { 0f, 0f, 1f };
        _playerHeadUp = new float[] { 0f, 0f, 0f };
    }

    IEnumerator GetRequest()
    {
        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.isNetworkError)
            {
                lastRequestFailed = true;
                SetDefaultListenerPosition();
            }
            else
            {
                lastRequestFailed = false;
                try
                {
                    ApplyFrame(JsonUtility.FromJson<Frame>(webRequest.downloadHandler.text));
                }
                catch (Exception)
                {
                    SetDefaultListenerPosition();
                }
            }
        }
    }

    // Update is called once per frame
    // bool UpdatePlayerPos()
    // {        
    //     using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
    //     {
    //         // Request and wait for the desired page.
    //         yield return webRequest.SendWebRequest();

    //         if (webRequest.isNetworkError)
    //         {
    //             Debug.Log(": Error: " + webRequest.error);
    //         }
    //         else
    //         {
    //             Debug.Log(":\nReceived API Frame ");
    //             string resp = webRequest.downloadHandler.text;
    //             try{
    //                 Frame foundFrame = JsonUtility.FromJson<Frame>(resp);
    //             }catch(Exception e){
    //                 Debug.Log(e);
    //             }
    //         }
    //     }
    //     return true;
    // }
}
