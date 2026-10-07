using System;
using System.Collections.Generic;
using System.IO;

namespace NetSwitchPlugin
{
    // Cloudflare WARP is controlled through warp-cli, which talks to the WARP service
    // and does not need administrator rights.
    internal static class Warp
    {
        private const int CommandTimeoutMs = 15000;

        private static readonly string[] KnownModes = { "warp", "doh", "warp+doh", "dot", "warp+dot", "proxy", "tunnel_only" };

        public static string ResolveCli(Dictionary<string, object> settings)
        {
            string configured = Json.GetTrimmed(settings, "warpCliPath");
            if (configured.Length > 0) return File.Exists(configured) ? configured : null;

            string[] candidates =
            {
                Path.Combine(Shell.ProgramFiles(), @"Cloudflare\Cloudflare WARP\warp-cli.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Cloudflare\Cloudflare WARP\warp-cli.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static LinkState GetState(Dictionary<string, object> settings)
        {
            string cli = ResolveCli(settings);
            if (cli == null) return LinkState.NotFound;

            CommandResult result = RunCli(cli, "status");
            if (!result.Started || result.TimedOut) return LinkState.Error;
            return ParseStatus(result.Output);
        }

        public static LinkState ParseStatus(string output)
        {
            string text = (output ?? string.Empty).ToLowerInvariant();
            foreach (string line in text.Split('\n'))
            {
                int marker = line.IndexOf("status update:", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    text = line.Substring(marker + "status update:".Length);
                    break;
                }
            }

            if (text.Contains("disconnected") || text.Contains("paused")) return LinkState.Off;
            if (text.Contains("connecting")) return LinkState.Busy;
            if (text.Contains("connected")) return LinkState.On;
            return LinkState.Error;
        }

        public static bool Press(Dictionary<string, object> settings)
        {
            string cli = ResolveCli(settings);
            if (cli == null)
            {
                Log.Write("WARP: warp-cli.exe not found.");
                return false;
            }

            string mode = Json.GetString(settings, "mode", "toggle");
            bool connect;
            if (mode == "on") connect = true;
            else if (mode == "off") connect = false;
            else
            {
                LinkState current = ParseStatus(RunCli(cli, "status").Output);
                connect = current != LinkState.On && current != LinkState.Busy;
            }

            if (connect)
            {
                string warpMode = Json.GetTrimmed(settings, "warpMode").ToLowerInvariant();
                if (Array.IndexOf(KnownModes, warpMode) >= 0 && !SetMode(cli, warpMode))
                {
                    Log.Write("WARP: cannot switch mode to " + warpMode + ".");
                    return false;
                }
            }

            CommandResult result = RunCli(cli, connect ? "connect" : "disconnect");
            if (result.ExitCode != 0)
            {
                Log.Write("WARP: " + (connect ? "connect" : "disconnect") + " failed: " + result.Output.Trim());
                return false;
            }

            Log.Write("WARP: " + (connect ? "connect" : "disconnect") + " requested.");
            return true;
        }

        private static bool SetMode(string cli, string warpMode)
        {
            // Current clients use "mode", older ones "set-mode".
            if (RunCli(cli, "mode " + warpMode).ExitCode == 0) return true;
            return RunCli(cli, "set-mode " + warpMode).ExitCode == 0;
        }

        private static CommandResult RunCli(string cli, string arguments)
        {
            // Without an accepted ToS warp-cli asks interactively and never finishes.
            CommandResult result = Shell.Run(cli, "--accept-tos " + arguments, CommandTimeoutMs);
            if (result.Started && result.ExitCode != 0 && RejectsAcceptTos(result.Output))
            {
                result = Shell.Run(cli, arguments, CommandTimeoutMs);
            }
            return result;
        }

        public static bool RejectsAcceptTos(string output)
        {
            string text = (output ?? string.Empty).ToLowerInvariant();
            return text.Contains("accept-tos") &&
                (text.Contains("unexpected") || text.Contains("found argument") || text.Contains("unrecognized"));
        }
    }
}
