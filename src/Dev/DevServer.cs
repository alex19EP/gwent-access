#if DEBUG
using System;
using System.IO;
using System.Threading;

namespace GwentAccess.Dev
{
    /// <summary>
    /// Debug-only loopback HTTP server for inspecting and driving the live game from a terminal:
    ///   POST /eval            body = C# code, run on the game's main thread; REPL state persists between calls.
    ///   POST /eval/reset      forget earlier variables.
    ///   GET  /speech?since=N  lines the mod spoke since cursor N (the last line says the cursor to use next).
    ///   GET  /health          liveness.
    ///   POST /reload          dispose the module and load the rebuilt Module\&lt;ModName&gt;.Module.dll (main thread).
    ///   GET  /module          which module generation and build is loaded.
    /// Lives in the host so it survives module reloads. Off unless the marker file BepInEx\plugins\&lt;ModName&gt;\devserver.enable
    /// exists (or &lt;EnvPrefix&gt;_DEV=1, e.g. GWENTACCESS_DEV=1). Port and prefix per game: <see cref="GameInfo"/>.
    /// A file survives relaunching through the launcher, an environment variable doesn't.
    /// Release builds don't contain any of this.
    /// </summary>
    public static class DevServer
    {
        /// <summary>The module points Speech.Observer here on Load, so /speech sees every line it speaks.</summary>
        public static void TapSpeech(string text) => Tap.Add(text);

        private const int DefaultPort = GameInfo.DevPort;
        private static readonly SpeechTap Tap = new SpeechTap();
        private static readonly CSharpEvaluator Evaluator = new CSharpEvaluator();
        private static DevHttpServer _http;

        public static void Start(string pluginDir)
        {
            bool marker = File.Exists(Path.Combine(pluginDir, "devserver.enable"));
            if (!marker && Environment.GetEnvironmentVariable(GameInfo.EnvPrefix + "_DEV") != "1") return;
            int port = DefaultPort;
            int.TryParse(Environment.GetEnvironmentVariable(GameInfo.EnvPrefix + "_DEV_PORT") ?? "", out port);
            if (port <= 0) port = DefaultPort;
            try
            {
                _http = new DevHttpServer(port, Handle);
                _http.Start();
                Plugin.LogInfo("Dev server on http://localhost:" + port + " (" + _http.BoundAddresses + "): POST /eval, POST /eval/reset, GET /speech?since=N, GET /health, POST /reload, GET /module");
            }
            catch (Exception e) { Plugin.LogInfo("Dev server failed to start: " + e); }
        }

        private static string Handle(string method, string path, string body)
        {
            string route = path, query = "";
            int q = path.IndexOf('?');
            if (q >= 0) { route = path.Substring(0, q); query = path.Substring(q + 1); }

            if (route == "/health") return "ok\n";
            if (route == "/speech")
            {
                long since = 0;
                foreach (var kv in query.Split('&'))
                    if (kv.StartsWith("since=")) long.TryParse(kv.Substring(6), out since);
                string lines = Tap.Render(since, out long next);
                return lines + "next=" + next + "\n";
            }
            if (route == "/eval/reset" && method == "POST") { Evaluator.Reset(); return "(reset)\n"; }
            if (route == "/module") return ModuleLoader.Describe();
            if (route == "/reload" && method == "POST")
                return OnMainThread(() =>
                {
                    string result = ModuleLoader.Reload();
                    Evaluator.Rebind(); // the next eval references the new generation
                    return result;
                }, 60000);
            if (route == "/eval" && method == "POST")
            {
                if (string.IsNullOrWhiteSpace(body)) return "(empty body)\n";
                var script = Evaluator.Compile(body, out string errors);
                if (script == null) return errors;
                return OnMainThread(() => Evaluator.Run(script), 30000);
            }
            return "unknown route " + method + " " + route + "\n";
        }

        /// <summary>Run work on the game thread through MainThread's queue and wait for it.</summary>
        private static string OnMainThread(Func<string> work, int timeoutMs)
        {
            string result = null;
            using (var done = new ManualResetEventSlim(false))
            {
                MainThread.Post(() =>
                {
                    // Keep frames (and so this queue) running while a terminal has focus instead of the game.
                    try { UnityEngine.Application.runInBackground = true; } catch { }
                    try { result = work(); }
                    catch (Exception e) { result = "[exception] " + e + "\n"; }
                    finally { try { done.Set(); } catch (ObjectDisposedException) { } }
                });
                if (!done.Wait(timeoutMs))
                    return "[timeout] the game thread didn't run the code within " + timeoutMs / 1000 + " s. Is the game paused or still loading? It may still run later.\n";
            }
            return result;
        }
    }
}
#endif
