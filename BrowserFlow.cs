// Interaktiver Browser-Flow ohne eingebettete Browser-Komponente:
// startet einen TcpListener auf der konfigurierten Redirect-URI (Loopback,
// kein Admin/keine URL-Reservierung noetig), oeffnet den System-Browser und
// faengt den Redirect (Query bei OAuth/OIDC oder POST-Body bei SAML) ab.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AdfsTester
{
    public sealed class CapturedResponse
    {
        public bool Success;
        public string Error = "";
        public readonly Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string RawRequest = "";
        public string Method = "";
    }

    public static class BrowserFlow
    {
        // Bindet auf die Redirect-URI, oeffnet authUrl im Browser und wartet auf
        // den eingehenden Redirect (oder Timeout).
        public static CapturedResponse Capture(string redirectUri, string authUrl, int timeoutSec)
        {
            var res = new CapturedResponse();
            Uri uri;
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out uri))
            { res.Error = "Redirect-URI ungueltig: " + redirectUri; return res; }

            int port = uri.Port;
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception ex)
            {
                res.Error = "Konnte lokalen Listener auf 127.0.0.1:" + port + " nicht starten: " + ex.Message +
                            (port < 1024 ? " (Ports < 1024 erfordern oft Adminrechte - hohen Port in der Redirect-URI verwenden.)" : "");
                return res;
            }

            try
            {
                try { Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true }); }
                catch (Exception ex) { res.Error = "Browser konnte nicht gestartet werden: " + ex.Message; return res; }

                // Auf eingehende Verbindung warten (mit Timeout)
                var ar = listener.BeginAcceptTcpClient(null, null);
                if (!ar.AsyncWaitHandle.WaitOne(Math.Max(5, timeoutSec) * 1000))
                {
                    res.Error = "Timeout: kein Redirect innerhalb von " + Math.Max(5, timeoutSec) + "s empfangen. " +
                                "Login abgebrochen oder Redirect-URI stimmt nicht mit der ADFS-Registrierung ueberein?";
                    return res;
                }

                using (var client = listener.EndAcceptTcpClient(ar))
                using (var stream = client.GetStream())
                {
                    client.ReceiveTimeout = 5000;
                    var raw = ReadRequest(stream);
                    res.RawRequest = ErrorLogger.Truncate(raw, 4000);
                    ParseRequest(raw, res);
                    WriteResponse(stream, res);
                    res.Success = res.Params.Count > 0;
                    if (!res.Success && res.Error.Length == 0)
                        res.Error = "Redirect empfangen, aber keine Parameter (Query/Form) gefunden.";
                }
            }
            catch (Exception ex)
            {
                res.Error = "Fehler beim Empfang des Redirects: " + ex.Message;
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
            return res;
        }

        private static string ReadRequest(NetworkStream stream)
        {
            var sb = new StringBuilder();
            var buf = new byte[4096];
            int total = 0;
            // Header lesen bis CRLFCRLF
            while (true)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                total += n;
                var s = sb.ToString();
                int headerEnd = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd >= 0)
                {
                    // Bei POST: Content-Length-Body nachlesen
                    int cl = ContentLength(s);
                    int bodyHave = s.Length - (headerEnd + 4);
                    while (cl > 0 && bodyHave < cl)
                    {
                        int m = stream.Read(buf, 0, buf.Length);
                        if (m <= 0) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, m));
                        bodyHave += m;
                    }
                    break;
                }
                if (total > 1024 * 1024) break; // Schutz
            }
            return sb.ToString();
        }

        private static int ContentLength(string headers)
        {
            foreach (var line in headers.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(t.Substring("Content-Length:".Length).Trim(), out v)) return v;
                }
            }
            return 0;
        }

        private static void ParseRequest(string raw, CapturedResponse res)
        {
            if (string.IsNullOrEmpty(raw)) return;
            var firstLineEnd = raw.IndexOf("\r\n", StringComparison.Ordinal);
            var requestLine = firstLineEnd >= 0 ? raw.Substring(0, firstLineEnd) : raw;
            var tokens = requestLine.Split(' ');
            if (tokens.Length >= 2)
            {
                res.Method = tokens[0];
                var path = tokens[1];
                int q = path.IndexOf('?');
                if (q >= 0) ParseUrlEncoded(path.Substring(q + 1), res.Params);
            }
            // POST-Body
            int bodyStart = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (bodyStart >= 0)
            {
                var body = raw.Substring(bodyStart + 4);
                if (body.Length > 0) ParseUrlEncoded(body, res.Params);
            }
        }

        private static void ParseUrlEncoded(string s, Dictionary<string, string> into)
        {
            foreach (var pair in s.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = eq >= 0 ? pair.Substring(0, eq) : pair;
                string v = eq >= 0 ? pair.Substring(eq + 1) : "";
                try { k = Uri.UnescapeDataString(k.Replace('+', ' ')); } catch { }
                try { v = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
                if (!into.ContainsKey(k)) into[k] = v;
            }
        }

        private static void WriteResponse(NetworkStream stream, CapturedResponse res)
        {
            string status = res.Params.Count > 0 ? "Antwort empfangen" : "Keine Parameter empfangen";
            string color = res.Params.ContainsKey("error") || res.Params.Count == 0 ? "#c42b1c" : "#107c10";
            var html =
                "<!doctype html><html><head><meta charset='utf-8'><title>ADFS-Tester</title></head>" +
                "<body style='font-family:Segoe UI,Arial;text-align:center;margin-top:60px'>" +
                "<h2 style='color:" + color + "'>ADFS-Tester: " + status + "</h2>" +
                "<p>Sie koennen dieses Fenster jetzt schliessen und zum Tool zurueckkehren.</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            var header =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Content-Length: " + bytes.Length + "\r\n" +
                "Connection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(header);
            stream.Write(hb, 0, hb.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }
}
