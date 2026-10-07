using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NetSwitchPlugin
{
    internal static class Program
    {
        public const string WarpAction = "com.local.netswitch.warp";
        public const string HappAction = "com.local.netswitch.happ";
        public const string ZapretAction = "com.local.netswitch.zapret";

        private const int PollIntervalMs = 3000;

        private sealed class Button
        {
            public string Context;
            public string Action;
            public Dictionary<string, object> Settings;
            public int Busy;
            public int LastState = -1;
            public string LastTitle;
        }

        public static readonly string PluginDirectory = AppDomain.CurrentDomain.BaseDirectory;

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly object ButtonsGate = new object();
        private static readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);

        private static ClientWebSocket _socket;
        private static readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);
        private static CancellationToken _token;
        private static int _polling;

        [STAThread]
        private static int Main(string[] args)
        {
            Log.Path = Path.Combine(PluginDirectory, "netswitch.log");
            if (HasArgument(args, "--self-test")) return SelfTest.Run();

            int port;
            string pluginUuid = GetArgument(args, "-pluginUUID");
            string registerEvent = GetArgument(args, "-registerEvent");
            if (!int.TryParse(GetArgument(args, "-port"), out port) ||
                string.IsNullOrWhiteSpace(pluginUuid) ||
                string.IsNullOrWhiteSpace(registerEvent))
            {
                Log.Write("Startup failed: StreamDock registration arguments are missing.");
                return 2;
            }

            using (CancellationTokenSource shutdown = new CancellationTokenSource())
            using (Timer poll = new Timer(delegate { PollAll(); }, null, Timeout.Infinite, Timeout.Infinite))
            {
                _token = shutdown.Token;
                try
                {
                    Log.Write("Starting plugin.");
                    RunAsync(port, pluginUuid, registerEvent, poll).GetAwaiter().GetResult();
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Write("Fatal error: " + ex);
                    return 1;
                }
                finally
                {
                    poll.Change(Timeout.Infinite, Timeout.Infinite);
                    shutdown.Cancel();
                    if (_socket != null) _socket.Dispose();
                    Log.Write("Plugin stopped.");
                }
            }
        }

        private static async Task RunAsync(int port, string pluginUuid, string registerEvent, Timer poll)
        {
            _socket = new ClientWebSocket();
            // Skips system proxy auto-detection, which delays a connection to localhost by seconds.
            _socket.Options.Proxy = null;
            await _socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port), _token).ConfigureAwait(false);
            await SendAsync(new Dictionary<string, object> { { "event", registerEvent }, { "uuid", pluginUuid } }).ConfigureAwait(false);
            Log.Write("Connected to StreamDock on port " + port + ".");

            poll.Change(PollIntervalMs, PollIntervalMs);
            while (_socket.State == WebSocketState.Open && !_token.IsCancellationRequested)
            {
                string message = await ReceiveTextAsync().ConfigureAwait(false);
                if (message == null) break;

                try
                {
                    HandleMessage(message);
                }
                catch (Exception ex)
                {
                    Log.Write("Message handler error: " + ex.Message);
                }
            }
        }

        private static async Task<string> ReceiveTextAsync()
        {
            byte[] buffer = new byte[8192];
            using (MemoryStream stream = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _token).ConfigureAwait(false);
                    }
                    catch (WebSocketException)
                    {
                        // StreamDock drops the connection without a close handshake when it exits.
                        Log.Write("StreamDock closed the connection.");
                        return null;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log.Write("StreamDock closed the connection.");
                        return null;
                    }

                    if (result.MessageType == WebSocketMessageType.Text) stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        private static void HandleMessage(string raw)
        {
            Dictionary<string, object> message = Serializer.DeserializeObject(raw) as Dictionary<string, object>;
            if (message == null) return;

            string eventName = Json.GetString(message, "event", string.Empty);
            string context = Json.GetString(message, "context", string.Empty);
            string action = Json.GetString(message, "action", string.Empty);
            Dictionary<string, object> payload = Json.GetDictionary(message, "payload");
            Dictionary<string, object> settings = Json.GetDictionary(payload, "settings");
            if (context.Length == 0) return;

            switch (eventName)
            {
                case "willAppear":
                case "didReceiveSettings":
                {
                    Button button = Remember(context, action, settings);
                    if (button != null) Background(RefreshAsync(button, true));
                    break;
                }
                case "willDisappear":
                    lock (ButtonsGate) Buttons.Remove(context);
                    break;
                case "keyDown":
                {
                    Button button = Remember(context, action, settings);
                    if (button != null) Background(PressAsync(button, settings));
                    break;
                }
                case "sendToPlugin":
                    Background(AnswerInspectorAsync(context, action, payload));
                    break;
            }
        }

        private static Button Remember(string context, string action, Dictionary<string, object> settings)
        {
            if (action != WarpAction && action != HappAction && action != ZapretAction) return null;

            lock (ButtonsGate)
            {
                Button button;
                if (!Buttons.TryGetValue(context, out button))
                {
                    button = new Button { Context = context, Action = action };
                    Buttons[context] = button;
                }
                button.Settings = settings;
                return button;
            }
        }

        private static async Task PressAsync(Button button, Dictionary<string, object> settings)
        {
            if (Interlocked.CompareExchange(ref button.Busy, 1, 0) != 0) return;

            bool ok = false;
            try
            {
                await SetTitleAsync(button, "…").ConfigureAwait(false);
                ok = await Task.Run(() => Execute(button.Action, settings)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Write("Action failed: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref button.Busy, 0);
            }

            await SendAsync(new Dictionary<string, object>
            {
                { "event", ok ? "showOk" : "showAlert" },
                { "context", button.Context }
            }).ConfigureAwait(false);

            // Connections need a moment to change state; the poll timer keeps updating afterwards.
            await Task.Delay(800).ConfigureAwait(false);
            await RefreshAsync(button, true).ConfigureAwait(false);
        }

        private static bool Execute(string action, Dictionary<string, object> settings)
        {
            switch (action)
            {
                case WarpAction: return Warp.Press(settings);
                case HappAction: return Happ.Press(settings);
                case ZapretAction: return Zapret.Press(settings);
                default: return false;
            }
        }

        private static LinkState GetState(string action, Dictionary<string, object> settings)
        {
            try
            {
                switch (action)
                {
                    case WarpAction: return Warp.GetState(settings);
                    case HappAction: return Happ.GetState(settings);
                    case ZapretAction: return Zapret.GetState(settings);
                    default: return LinkState.Unknown;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Status check failed: " + ex.Message);
                return LinkState.Error;
            }
        }

        private static async Task RefreshAsync(Button button, bool force)
        {
            if (Volatile.Read(ref button.Busy) != 0) return;
            LinkState state = await Task.Run(() => GetState(button.Action, button.Settings)).ConfigureAwait(false);
            await ShowStateAsync(button, state, force).ConfigureAwait(false);
        }

        private static void PollAll()
        {
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;
            try
            {
                List<Button> snapshot;
                lock (ButtonsGate) snapshot = new List<Button>(Buttons.Values);

                // Buttons with the same service and settings share one status check per tick.
                Dictionary<string, LinkState> cache = new Dictionary<string, LinkState>(StringComparer.Ordinal);
                foreach (Button button in snapshot)
                {
                    if (Volatile.Read(ref button.Busy) != 0) continue;

                    string key = button.Action + "|" + Serializer.Serialize(button.Settings);
                    LinkState state;
                    if (!cache.TryGetValue(key, out state))
                    {
                        state = GetState(button.Action, button.Settings);
                        cache[key] = state;
                    }
                    ShowStateAsync(button, state, false).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Log.Write("Status poll failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private static async Task ShowStateAsync(Button button, LinkState state, bool force)
        {
            int keyState = state == LinkState.On ? 1 : 0;
            if (force || keyState != button.LastState)
            {
                button.LastState = keyState;
                await SendAsync(new Dictionary<string, object>
                {
                    { "event", "setState" },
                    { "context", button.Context },
                    { "payload", new Dictionary<string, object> { { "state", keyState } } }
                }).ConfigureAwait(false);
            }

            string title = Json.GetBool(button.Settings, "showStatus", true) ? StateLabel(state) : string.Empty;
            if (force || title != button.LastTitle) await SetTitleAsync(button, title).ConfigureAwait(false);
        }

        private static Task SetTitleAsync(Button button, string title)
        {
            button.LastTitle = title;
            return SendAsync(new Dictionary<string, object>
            {
                { "event", "setTitle" },
                { "context", button.Context },
                { "payload", new Dictionary<string, object> { { "title", title }, { "target", 0 } } }
            });
        }

        public static string StateLabel(LinkState state)
        {
            switch (state)
            {
                case LinkState.On: return "ВКЛ";
                case LinkState.Off: return "ВЫКЛ";
                case LinkState.Busy: return "…";
                case LinkState.NotFound: return "НЕТ";
                case LinkState.NotConfigured: return "НАСТР.";
                case LinkState.Error: return "ОШИБКА";
                default: return "?";
            }
        }

        private static async Task AnswerInspectorAsync(string context, string action, Dictionary<string, object> payload)
        {
            string request = Json.GetString(payload, "request", string.Empty);
            Dictionary<string, object> settings = Json.GetDictionary(payload, "settings");
            string notice = null;

            if (action == ZapretAction && request == "installTasks")
            {
                bool ok = await Task.Run(() => Zapret.InstallTasks()).ConfigureAwait(false);
                notice = ok ? "Задачи созданы, кнопка будет работать без запроса UAC." : "Задачи не созданы. Подробности в netswitch.log.";
            }
            else if (action == ZapretAction && request == "removeTasks")
            {
                bool ok = await Task.Run(() => Zapret.RemoveTasks()).ConfigureAwait(false);
                notice = ok ? "Задачи удалены." : "Задачи не удалены. Подробности в netswitch.log.";
            }
            else if (request != "info")
            {
                return;
            }

            Dictionary<string, object> info = await Task.Run(() => DescribeEnvironment(action, settings)).ConfigureAwait(false);
            if (notice != null) info["notice"] = notice;

            await SendAsync(new Dictionary<string, object>
            {
                { "event", "sendToPropertyInspector" },
                { "action", action },
                { "context", context },
                { "payload", info }
            }).ConfigureAwait(false);
        }

        private static Dictionary<string, object> DescribeEnvironment(string action, Dictionary<string, object> settings)
        {
            Dictionary<string, object> info = new Dictionary<string, object> { { "type", "info" } };
            switch (action)
            {
                case WarpAction:
                    info["warpCli"] = Warp.ResolveCli(settings) ?? string.Empty;
                    break;
                case HappAction:
                    info["happExe"] = Happ.ResolveExe(settings) ?? string.Empty;
                    break;
                case ZapretAction:
                {
                    string directory = Json.GetTrimmed(settings, "zapretDir");
                    string service = Json.GetTrimmed(settings, "serviceName");
                    info["strategies"] = Zapret.ListStrategies(directory);
                    info["winwsFound"] = Zapret.WinwsPresent(directory);
                    info["serviceFound"] = Zapret.ServiceStatus(service.Length == 0 ? "zapret" : service) != null;
                    info["tasksInstalled"] = Zapret.TasksInstalled();
                    break;
                }
            }
            info["state"] = StateLabel(GetState(action, settings));
            return info;
        }

        private static async Task SendAsync(Dictionary<string, object> message)
        {
            ClientWebSocket socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;

            byte[] bytes = Encoding.UTF8.GetBytes(Serializer.Serialize(message));
            await SendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (!_token.IsCancellationRequested) Log.Write("Send failed: " + ex.Message);
            }
            finally
            {
                SendGate.Release();
            }
        }

        private static void Background(Task task)
        {
            task.ContinueWith(
                t => Log.Write("Background task failed: " + t.Exception.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private static bool HasArgument(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetArgument(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }
    }
}
