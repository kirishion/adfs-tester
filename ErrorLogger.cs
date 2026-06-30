// Zentrale Fehler-Interpretation: wandelt Exceptions in verstaendliche
// Detail-/Empfehlungstexte um und liefert fertige CheckResult-Eintraege.
// So zeigt jeder Tester konkrete Fehlerquellen statt nur "Exception".

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace AdfsTester
{
    public static class ErrorLogger
    {
        // Wandelt eine Exception in einen Error-CheckResult mit Klartext-Hinweis.
        public static CheckResult ToCheckResult(string step, string context, Exception ex)
        {
            string detail = context + ": " + ex.GetType().Name + " - " + ex.Message;
            string rec = Recommendation(ex);
            string raw = BuildRaw(context, ex);
            return new CheckResult(step, Severity.Error, detail, rec, raw);
        }

        public static string Recommendation(Exception ex)
        {
            var se = FindSocketException(ex);
            if (se != null) return InterpretSocketError(se.SocketErrorCode);

            var we = ex as WebException;
            if (we != null)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr != null)
                    return "HTTP " + (int)hr.StatusCode + " " + hr.StatusCode +
                           " - Antwort-Body und Endpoint pruefen (siehe Rohdaten).";
                return InterpretWebExceptionStatus(we.Status);
            }

            if (ex is AuthenticationException)
                return "TLS/SSL-Handshake fehlgeschlagen: Zertifikat nicht vertrauenswuerdig, " +
                       "abgelaufen, Hostname-Mismatch oder TLS-Version vom Server deaktiviert.";
            if (ex is TimeoutException)
                return "Timeout - Server hat nicht rechtzeitig geantwortet. Firewall/Proxy pruefen.";
            if (ex is UriFormatException)
                return "URL ungueltig - AdfsHost / Endpoint-Schreibweise pruefen.";
            if (ex is System.Xml.XmlException)
                return "Antwort ist kein gueltiges XML - moeglicherweise HTML-Fehlerseite oder falscher Pfad.";
            if (ex is FormatException)
                return "Ungueltiges Datenformat (z.B. Base64/JWT/Zahl).";
            if (ex is UnauthorizedAccessException)
                return "Keine Berechtigung (Datei/Resource).";
            return "Details siehe Rohdaten.";
        }

        public static string BuildRaw(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Wo      : " + context);
            sb.AppendLine("Typ     : " + ex.GetType().FullName);
            sb.AppendLine("Meldung : " + ex.Message);

            var se = ex as SocketException;
            if (se != null)
                sb.AppendLine("Socket  : " + se.SocketErrorCode + " (Code " + se.ErrorCode + ")");

            var we = ex as WebException;
            if (we != null)
            {
                sb.AppendLine("WebStat : " + we.Status);
                var hr = we.Response as HttpWebResponse;
                if (hr != null)
                {
                    sb.AppendLine("HttpCode: " + (int)hr.StatusCode + " " + hr.StatusCode);
                    AppendHeader(sb, hr, "x-ms-correlation-id");
                    AppendHeader(sb, hr, "x-ms-request-id");
                    AppendHeader(sb, hr, "WWW-Authenticate");
                    try
                    {
                        using (var rs = hr.GetResponseStream())
                        using (var rd = new StreamReader(rs))
                        {
                            var body = rd.ReadToEnd();
                            if (!string.IsNullOrEmpty(body))
                                sb.AppendLine("Body    :" + Environment.NewLine + Truncate(body, 4000));
                        }
                    }
                    catch { /* Body evtl. nicht lesbar */ }
                }
            }

            Exception inner = ex.InnerException;
            int depth = 1;
            while (inner != null && depth <= 3)
            {
                sb.AppendLine("--- Inner " + depth + " ---");
                sb.AppendLine("  Typ    : " + inner.GetType().FullName);
                sb.AppendLine("  Meldung: " + inner.Message);
                var ise = inner as SocketException;
                if (ise != null)
                    sb.AppendLine("  Socket : " + ise.SocketErrorCode);
                inner = inner.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        private static void AppendHeader(StringBuilder sb, HttpWebResponse hr, string name)
        {
            try
            {
                var v = hr.Headers[name];
                if (!string.IsNullOrEmpty(v)) sb.AppendLine(("  " + name).PadRight(24) + ": " + v);
            }
            catch { }
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + " ... [gekuerzt, " + s.Length + " Zeichen]";
        }

        private static SocketException FindSocketException(Exception ex)
        {
            while (ex != null)
            {
                var se = ex as SocketException;
                if (se != null) return se;
                ex = ex.InnerException;
            }
            return null;
        }

        public static string InterpretSocketError(SocketError err)
        {
            switch (err)
            {
                case SocketError.HostNotFound:
                    return "DNS konnte den Host nicht aufloesen (NXDOMAIN). Tippfehler im AdfsHost? DNS erreichbar?";
                case SocketError.TryAgain:
                    return "DNS-Lookup temporaer fehlgeschlagen. Spaeter erneut versuchen.";
                case SocketError.HostUnreachable:
                    return "Host nicht erreichbar (Routing/Firewall).";
                case SocketError.NetworkUnreachable:
                    return "Netzwerk nicht erreichbar (Gateway/Subnetz).";
                case SocketError.ConnectionRefused:
                    return "Verbindung aktiv abgelehnt - Port 443 zu, ADFS-Dienst gestoppt oder Firewall blockiert.";
                case SocketError.ConnectionReset:
                    return "Verbindung zurueckgesetzt (TCP RST). Firewall/Load-Balancer/Idle-Timeout.";
                case SocketError.TimedOut:
                    return "Timeout - keine Antwort. Firewall verwirft Pakete? Proxy noetig?";
                case SocketError.AccessDenied:
                    return "Zugriff verweigert - lokale Firewall oder Proxy-Policy.";
                default:
                    return "Socket-Fehler (" + err + "). Firewall, Proxy und Konnektivitaet pruefen.";
            }
        }

        public static string InterpretWebExceptionStatus(WebExceptionStatus s)
        {
            switch (s)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    return "DNS-Aufloesung fehlgeschlagen. AdfsHost korrekt? DNS erreichbar?";
                case WebExceptionStatus.ConnectFailure:
                    return "TCP-Connect fehlgeschlagen. Proxy noetig? Firewall blockiert Port 443?";
                case WebExceptionStatus.SendFailure:
                    return "Senden des Requests fehlgeschlagen (Verbindung unterbrochen).";
                case WebExceptionStatus.ReceiveFailure:
                    return "Empfang der Antwort fehlgeschlagen (Server schloss Verbindung).";
                case WebExceptionStatus.Timeout:
                    return "HTTP-Timeout. Endpoint langsam oder nicht erreichbar?";
                case WebExceptionStatus.TrustFailure:
                    return "TLS-Vertrauensfehler: Server-Zertifikat nicht vertrauenswuerdig (Root fehlt/Self-Signed).";
                case WebExceptionStatus.SecureChannelFailure:
                    return "TLS-Handshake fehlgeschlagen: Protokoll-/Cipher-Mismatch oder Zertifikatproblem.";
                case WebExceptionStatus.ProtocolError:
                    return "HTTP-Fehlercode (siehe Rohdaten/Body).";
                default:
                    return "WebException (" + s + ").";
            }
        }
    }
}
