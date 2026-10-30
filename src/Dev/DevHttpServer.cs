#if DEBUG
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GwentAccess.Dev
{
    /// <summary>
    /// Minimal loopback HTTP/1.1 server on raw TcpListeners: request line, Content-Length body, one response,
    /// connection close. Enough for curl. Avoids HttpListener, which on .NET goes through http.sys and needs a
    /// URL ACL or admin. Ported from RTAccess. DEBUG-only.
    ///
    /// Listens on both 127.0.0.1 and ::1. Address it as localhost: Windows resolves that to ::1 first, and the
    /// IPv4 loopback on the dev box loses about one connection in three to a filter driver's resets.
    /// </summary>
    internal sealed class DevHttpServer
    {
        /// <summary>method, path with query, body -> response body.</summary>
        public delegate string RequestHandler(string method, string path, string body);

        private readonly int _port;
        private readonly RequestHandler _handler;
        private readonly List<TcpListener> _listeners = new List<TcpListener>();
        private readonly object _handleLock = new object();
        private volatile bool _running;

        public string BoundAddresses { get; private set; } = "";

        public DevHttpServer(int port, RequestHandler handler)
        {
            _port = port;
            _handler = handler;
        }

        public void Start()
        {
            // IPv4 is required (a failed bind throws to the caller); IPv6 is best effort.
            var v4 = new TcpListener(IPAddress.Loopback, _port);
            v4.Start();
            _listeners.Add(v4);
            var bound = new List<string> { "127.0.0.1" };
            if (Socket.OSSupportsIPv6)
            {
                try
                {
                    var v6 = new TcpListener(IPAddress.IPv6Loopback, _port);
                    v6.Start();
                    _listeners.Add(v6);
                    bound.Add("[::1]");
                }
                catch (SocketException e) { Plugin.LogInfo("Dev server: IPv6 loopback skipped: " + e.Message); }
            }
            BoundAddresses = string.Join(" + ", bound);

            _running = true;
            foreach (var listener in _listeners)
            {
                var own = listener;
                new Thread(() => Loop(own)) { IsBackground = true, Name = "GwentAccessDevHttp " + own.LocalEndpoint }.Start();
            }
        }

        private void Loop(TcpListener listener)
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    // One request at a time across both sockets: /eval shares one REPL state.
                    lock (_handleLock) Handle(client);
                }
                catch (Exception e) { if (_running) Plugin.LogInfo("Dev server: " + e.Message); }
                finally { if (client != null) { try { client.Close(); } catch { } } }
            }
        }

        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 15000;
            NetworkStream stream = client.GetStream();
            var data = new List<byte>();
            var buf = new byte[8192];

            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) return;
                for (int i = 0; i < n; i++) data.Add(buf[i]);
                headerEnd = IndexOfHeaderEnd(data);
                if (data.Count > 1 << 20) { Respond(client, stream, 400, "header too large\n"); return; }
            }

            string header = Encoding.ASCII.GetString(data.ToArray(), 0, headerEnd);
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            string method = requestLine.Length > 0 ? requestLine[0] : "";
            string path = requestLine.Length > 1 ? requestLine[1] : "/";

            int contentLength = 0;
            foreach (string line in lines)
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);

            int bodyStart = headerEnd + 4;
            while (data.Count - bodyStart < contentLength)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                for (int i = 0; i < n; i++) data.Add(buf[i]);
            }
            int have = Math.Min(contentLength, data.Count - bodyStart);
            string body = have > 0 ? Encoding.UTF8.GetString(data.ToArray(), bodyStart, have) : "";

            string response;
            try { response = _handler(method, path, body) ?? ""; }
            catch (Exception e) { Respond(client, stream, 500, "handler error: " + e + "\n"); return; }
            Respond(client, stream, 200, response);
        }

        private static int IndexOfHeaderEnd(List<byte> d)
        {
            for (int i = 0; i + 3 < d.Count; i++)
                if (d[i] == 13 && d[i + 1] == 10 && d[i + 2] == 13 && d[i + 3] == 10) return i;
            return -1;
        }

        /// <summary>
        /// Write the response, half-close, then drain until the client closes (or 2 s pass). Closing right after
        /// the write lets the loopback filter driver turn teardown into a reset that eats the reply.
        /// </summary>
        private static void Respond(TcpClient client, NetworkStream stream, int status, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            string head = "HTTP/1.1 " + status + (status == 200 ? " OK" : " ERROR") + "\r\n"
                + "Content-Type: text/plain; charset=utf-8\r\n"
                + "Content-Length: " + bodyBytes.Length + "\r\n"
                + "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
            try
            {
                client.Client.Shutdown(SocketShutdown.Send);
                client.ReceiveTimeout = 2000;
                var sink = new byte[1024];
                while (stream.Read(sink, 0, sink.Length) > 0) { }
            }
            catch { } // the peer closing, or the drain timing out, is the normal end
        }
    }
}
#endif
