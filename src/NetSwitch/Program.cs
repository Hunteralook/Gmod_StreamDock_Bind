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
        public const string HappAction = "com.local.netswitch.happ";
        public const string ServerAction = "com.local.netswitch.server";
        public const string SubscriptionAction = "com.local.netswitch.subscription";
        public const string ZapretAction = "com.local.netswitch.zapret";

        private const int PollIntervalMs = 1000;

        private sealed class Button
        {
            public string Context;
            public string Action;
            public Dictionary<string, object> Settings;
            public int Busy;
            public int Refreshing;
            public DateTime NextRefreshUtc;
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
                    if (button != null)
                    {
                        button.NextRefreshUtc = DateTime.MinValue;
                        Background(RefreshAsync(button, true));
                    }
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
            if (RefreshInterval(action) == TimeSpan.Zero) return null;

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

        private static TimeSpan RefreshInterval(string action)
        {
            switch (action)
            {
                case HappAction:
                case ZapretAction:
                    return TimeSpan.FromSeconds(3);
                case ServerAction:
                    return TimeSpan.FromSeconds(30);
                case SubscriptionAction:
                    // The subscription itself is downloaded at most every 6 hours; this only updates days left.
                    return TimeSpan.FromMinutes(1);
                default:
                    return TimeSpan.Zero;
            }
        }

        private static async Task PressAsync(Button button, Dictionary<string, object> settings)
        {
            if (Interlocked.CompareExchange(ref button.Busy, 1, 0) != 0) return;

            bool ok = false;
            Display display = null;
            try
            {
                await SetTitleAsync(button, "…").ConfigureAwait(false);
                ok = await Task.Run(() => Execute(button.Action, settings, out display)).ConfigureAwait(false);
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

            if (display != null)
            {
                button.NextRefreshUtc = DateTime.UtcNow + RefreshInterval(button.Action);
                await ShowAsync(button, display, true).ConfigureAwait(false);
                return;
            }

            // Connections need a moment to change state; the poll timer keeps updating afterwards.
            await Task.Delay(800).ConfigureAwait(false);
            await RefreshAsync(button, true).ConfigureAwait(false);
        }

        // Server and subscription buttons refresh their data on press and return what to show.
        private static bool Execute(string action, Dictionary<string, object> settings, out Display display)
        {
            display = null;
            switch (action)
            {
                case HappAction:
                    return Happ.Press(settings);
                case ZapretAction:
                    return Zapret.Press(settings);
                case ServerAction:
                    display = ServerButton.Refresh(settings);
                    return display.State == 1;
                case SubscriptionAction:
                    display = SubscriptionButton.Refresh(settings, true);
                    return Subscription.IsValidUrl(Json.GetTrimmed(settings, "subscriptionUrl")) &&
                        Subscription.Get(Json.GetTrimmed(settings, "subscriptionUrl"), false).Error == null;
                default:
                    return false;
            }
        }

        private static Display Describe(Button button)
        {
            Dictionary<string, object> settings = button.Settings;
            try
            {
                switch (button.Action)
                {
                    case HappAction:
                        return StatusDisplay(Happ.GetState(settings), settings);
                    case ZapretAction:
                        return StatusDisplay(Zapret.GetState(settings), settings);
                    case ServerAction:
                        return ServerButton.Refresh(settings);
                    case SubscriptionAction:
                        return SubscriptionButton.Refresh(settings, false);
                    default:
                        return new Display(0, "?");
                }
            }
            catch (Exception ex)
            {
                Log.Write("Status check failed: " + ex.Message);
                return new Display(0, StateLabel(LinkState.Error));
            }
        }

        private static Display StatusDisplay(LinkState state, Dictionary<string, object> settings)
        {
            return new Display(
                state == LinkState.On ? 1 : 0,
                Json.GetBool(settings, "showStatus", true) ? StateLabel(state) : string.Empty);
        }

        private static async Task RefreshAsync(Button button, bool force)
        {
            if (Volatile.Read(ref button.Busy) != 0) return;
            if (Interlocked.CompareExchange(ref button.Refreshing, 1, 0) != 0) return;
            try
            {
                button.NextRefreshUtc = DateTime.UtcNow + RefreshInterval(button.Action);
                Display display = await Task.Run(() => Describe(button)).ConfigureAwait(false);
                if (Volatile.Read(ref button.Busy) == 0) await ShowAsync(button, display, force).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref button.Refreshing, 0);
            }
        }

        private static void PollAll()
        {
            List<Button> due = new List<Button>();
            lock (ButtonsGate)
            {
                foreach (Button button in Buttons.Values)
                {
                    if (button.NextRefreshUtc <= DateTime.UtcNow) due.Add(button);
                }
            }

            // Every button refreshes on its own task, so a slow ping or download does not hold up the rest.
            foreach (Button button in due) Background(RefreshAsync(button, false));
        }

        private static async Task ShowAsync(Button button, Display display, bool force)
        {
            if (force || display.State != button.LastState)
            {
                button.LastState = display.State;
                await SendAsync(new Dictionary<string, object>
                {
                    { "event", "setState" },
                    { "context", button.Context },
                    { "payload", new Dictionary<string, object> { { "state", display.State } } }
                }).ConfigureAwait(false);
            }

            if (force || display.Title != button.LastTitle) await SetTitleAsync(button, display.Title).ConfigureAwait(false);
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
            else if (request != "info" && request != "reload")
            {
                return;
            }

            bool reload = request == "reload";
            Dictionary<string, object> info = await Task.Run(() => DescribeEnvironment(action, settings, reload)).ConfigureAwait(false);
            if (notice != null) info["notice"] = notice;

            await SendAsync(new Dictionary<string, object>
            {
                { "event", "sendToPropertyInspector" },
                { "action", action },
                { "context", context },
                { "payload", info }
            }).ConfigureAwait(false);
        }

        private static Dictionary<string, object> DescribeEnvironment(string action, Dictionary<string, object> settings, bool reload)
        {
            Dictionary<string, object> info = new Dictionary<string, object> { { "type", "info" } };
            Button probe = new Button { Action = action, Settings = settings };

            switch (action)
            {
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
                case ServerAction:
                case SubscriptionAction:
                {
                    string url = Json.GetTrimmed(settings, "subscriptionUrl");
                    if (url.Length == 0)
                    {
                        // Offer the link already entered on another button so it does not have to be pasted twice.
                        info["knownUrl"] = KnownSubscriptionUrl();
                        break;
                    }

                    SubscriptionInfo subscription = Subscription.Get(url, reload);
                    List<Dictionary<string, object>> servers = new List<Dictionary<string, object>>();
                    foreach (ServerEntry server in subscription.Servers)
                    {
                        servers.Add(new Dictionary<string, object>
                        {
                            { "name", server.Name },
                            { "host", server.Host },
                            { "port", server.Port },
                            { "udp", server.Udp }
                        });
                    }
                    info["servers"] = servers;
                    info["title"] = subscription.Title;
                    info["error"] = subscription.Error ?? string.Empty;
                    info["daysLeft"] = SubscriptionButton.DaysLeft(subscription);
                    info["trafficLeft"] = SubscriptionButton.TrafficLeft(subscription);
                    info["used"] = SubscriptionButton.FormatBytes(subscription.Upload + subscription.Download);
                    break;
                }
            }

            info["state"] = Describe(probe).Title.Replace("\n", " · ");
            return info;
        }

        private static string KnownSubscriptionUrl()
        {
            lock (ButtonsGate)
            {
                foreach (Button button in Buttons.Values)
                {
                    string url = Json.GetTrimmed(button.Settings, "subscriptionUrl");
                    if (url.Length > 0) return url;
                }
            }
            return string.Empty;
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
