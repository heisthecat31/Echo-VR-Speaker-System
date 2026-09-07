// MediaControl.exe - tiny bridge to Windows' System Media Transport Controls.
//
// Unity's Mono runtime cannot reach WinRT, so Echo Speaker System shells out to this the
// same way it shells out to AudioSwitch.exe.
//
//   MediaControl.exe pause
//       Pauses every media session that is currently Playing and writes the app id of
//       each one it paused to stdout, one per line.
//
//   MediaControl.exe play <appId> [<appId> ...]
//       Resumes only the given sessions. Passing back exactly what "pause" reported means
//       we never restart something the user paused themselves.
//
//   MediaControl.exe status
//       Writes "<status>|<appId>" per session. Diagnostic only.
//
// Exit codes: 0 = ok, 1 = no media session API available, 2 = bad arguments.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: MediaControl.exe pause|play <appId>...|status");
            return 2;
        }

        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            // Pre-1809 Windows, or the service is unavailable. Callers treat this as
            // "media control not supported" and carry on.
            Console.Error.WriteLine("media session manager unavailable: " + ex.Message);
            return 1;
        }
        if (manager == null)
        {
            return 1;
        }

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try
        {
            sessions = manager.GetSessions();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("could not enumerate sessions: " + ex.Message);
            return 1;
        }
        if (sessions == null)
        {
            return 0;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "pause":
                foreach (var session in sessions)
                {
                    if (StatusOf(session) != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        continue;
                    }
                    try
                    {
                        if (await session.TryPauseAsync())
                        {
                            Console.WriteLine(session.SourceAppUserModelId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("pause failed for "
                            + session.SourceAppUserModelId + ": " + ex.Message);
                    }
                }
                return 0;

            case "play":
                var wanted = new HashSet<string>(args.Skip(1), StringComparer.OrdinalIgnoreCase);
                foreach (var session in sessions)
                {
                    if (wanted.Count > 0 && !wanted.Contains(session.SourceAppUserModelId))
                    {
                        continue;
                    }
                    if (StatusOf(session) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        continue;
                    }
                    try
                    {
                        if (await session.TryPlayAsync())
                        {
                            Console.WriteLine(session.SourceAppUserModelId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("play failed for "
                            + session.SourceAppUserModelId + ": " + ex.Message);
                    }
                }
                return 0;

            case "status":
                foreach (var session in sessions)
                {
                    Console.WriteLine(StatusOf(session) + "|" + session.SourceAppUserModelId);
                }
                return 0;

            default:
                Console.Error.WriteLine("unknown command: " + args[0]);
                return 2;
        }
    }

    static GlobalSystemMediaTransportControlsSessionPlaybackStatus StatusOf(
        GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var info = session.GetPlaybackInfo();
            return info == null
                ? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed
                : info.PlaybackStatus;
        }
        catch
        {
            return GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
        }
    }
}
