# Echo-VR-Speaker-System

This is a system to take any audio stream (usually music) and make it sound like it is being played from "speakers" in a stadium outside of the Arena when you are playing. This is positional audio that adds realistic echo/reverberations as well as taking into account the geometry of the Arena itself so that it feels as though you are hearing your music from a stadium surrounding you.

![Speaker System App](https://github.com/iblowatsports/Echo-VR-Speaker-System/blob/main/EchoSpeakerSystemV0_4_0.png?raw=true)

  
 ## Requirements
A virtual audio cable is required - it is what Echo Speaker System captures from. Either
of these works, and the app detects both:
* **[VB-CABLE](https://vb-audio.com/Cable/)** (recommended, free) - set your music app's
  playback device to `CABLE Input (VB-Audio Virtual Cable)`.
* **[Virtual Audio Cable](https://vac.muzychenko.net/)** - set it to `Line 1 (Virtual
  Audio Cable)`.

If neither is installed, the app tells you which to get and shows a download button.

## Setup
 There are two methods to install and use Echo Speaker System: via Spark (Formerly Ignite Bot) or as a standalone app. Using Echo Speaker System from within Spark is **required for Quest users** and is also the only way to have custom goal horn support within Echo Speaker System.
 
 ### Spark
 * Go to https://ignitevr.gg/spark download, and install the latest install of Spark
 * In the in-game settings for Echo VR, make sure that "Enable API Access" is set to "Enabled"
 * Run Spark and click on the "Speaker System" tab. Click "Install Echo Speaker System" and follow the installation prompts
 * To use Echo Speaker System, run Spark and click "Start Speaker System" from within the "Speaker System" tab
 
 ### Standalone
 * Go to **[releases](https://github.com/iblowatsports/Echo-VR-Speaker-System/releases/latest)** and download the **Installer exe. Run this as administrator to install Echo Speaker System**.
 * In the in-game settings for Echo VR, make sure that "Enable API Access" is set to "Enabled"
 * Run Echo Speaker System
   * Select the application you would like played via the "speakers" (you may have to hit the refresh app list button if it does not show up). This will switch the selected app to be played via Echo Speaker System. 
* Any sound played from the selected application will now go through the virtual speakers surrounding the arena. Once you close the Echo Speaker System exe, the application you had selected will be switched back to the audio device it originally was using. Any time the Echo Speaker System exe is run in the future, it will attempt to automatically find and use the Application you had previously used with the Speaker System

## Note for Quest Users
In order to use this on Quest, you **must** install Spark and use Echo Speaker System from within Spark, as Spark handles finding and getting the API data from the Quest over the network. Also, in order to use this on Quest, you must have some way to get the audio from your PC to your Quest (ex: using a headset that supports wireless audio from your PC and wired audio from your Quest at the same time). **This app will not work if you do not have a way to listen to audio from your PC while playing on your Quest**

## Troubleshooting

**No sound, or the app opens with the audio input dropdown showing**
Echo Speaker System captures audio from a virtual audio cable. It detects both
[VB-CABLE](https://vb-audio.com/Cable/) and
[Virtual Audio Cable (VAC)](https://vac.muzychenko.net/) automatically, and if the cable
you previously used is gone but another one is installed it switches to that instead of
failing. If it cannot open any capture device it says so and leaves the input dropdown
visible so you can pick one by hand, rather than starting silently. Check that:
* A virtual audio cable is installed. If none is found, the app names a recommended one
  and shows a download button for it.
* Your music app's output is set to the cable in Windows' *App Volume and Device
  Settings*, and that the app is actually playing something.

**An app is stuck playing through the Virtual Audio Cable after a crash**
Echo Speaker System records which app it moved off which audio device, and puts them all
back when it exits. If it is killed before it can do that, just start it again - it
restores the saved endpoints on launch. Failing that, set the app's output device by hand
in Windows' *App Volume and Device Settings*.

**Settings are wrong and you want to start over**
Run the exe with the `-reset` argument to clear every saved setting (selected app, input
device, reverb presets, goal horn volume). Run it with `-selectinput` to force the audio
input dropdown to be shown.

**Custom goal horn isn't playing**
Custom goal horns require Spark - the standalone app hides the toggle. The file must be at
`GoalHorn.wav` next to the installed exe (by default
`C:\Program Files (x86)\Echo Speaker System\GoalHorn.wav`) and must be a valid WAV.

**Where are the logs?**
`%USERPROFILE%\AppData\LocalLow\Orbit\Echo Speaker System\Player.log`. Include this
file when reporting a bug.

## Building from source

Open the `Speaker System` folder in Unity **2019.4.4f1**. The solution and `.csproj` files
are generated by Unity on open and are not tracked in git, so you do not need to create
them yourself.
