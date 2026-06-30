// Schlanker HTTP-Client auf Basis von HttpWebRequest. Erfasst Status, Header
// und Body AUCH bei Fehlercodes (>=400), damit OAuth-/ADFS-Fehlermeldungen im
// Body sichtbar werden. Folgt KEINEN Redirects automatisch (wichtig fuer
// WS-Fed/SAML/OAuth, wo der Redirect selbst das Ergebnis ist).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AdfsTester
{
    public sealed class HttpResult
    {
        public bool Transport;            // true = HTTP-Antwort erhalten (auch 4xx/5xx)
        public int Status;                // HTTP-Statuscode, 0 wenn kein Transport
        public string StatusText = "";
        public WebHeaderCollection Headers = new WebHeaderCollection();
        public string Body = "";
        public string Location = "";      // Location-Header bei Redirect
        public Exception Error;           // gesetzt wenn Transport == false
        public string FinalUrl = "";

        public string Header(string name)
        {
            try { return Headers[name] ?? ""; } catch { return ""; }
        }
    }

    public static class HttpHelper
    {
        public static HttpResult Get(string url, int timeoutSec, bool allowAutoRedirect = false)
        {
            return Send("GET", url, null, null, timeoutSec, allowAutoRedirect, null, null);
        }

        public static HttpResult PostForm(string url, IDictionary<string, string> form,
                                          int timeoutSec, string basicUser = null, string basicPass = null)
        {
            var sb = new StringBuilder();
            foreach (var kv in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            return Send("POST", url, body, "application/x-www-form-urlencoded",
                        timeoutSec, false, basicUser, basicPass);
        }

        public static HttpResult PostRaw(string url, string body, string contentType, string soapAction, int timeoutSec)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            return Send("POST", url, bytes, contentType, timeoutSec, false, null, null, soapAction);
        }

        private static HttpResult Send(string method, string url, byte[] body, string contentType,
                                       int timeoutSec, bool allowAutoRedirect,
                                       string basicUser, string basicPass, string soapAction = null)
        {
            var res = new HttpResult { FinalUrl = url };
            HttpWebRequest req;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
            }
            catch (Exception ex)
            {
                res.Transport = false; res.Error = ex; return res;
            }

            req.Method = method;
            req.Timeout = Math.Max(1, timeoutSec) * 1000;
            req.ReadWriteTimeout = req.Timeout;
            req.AllowAutoRedirect = allowAutoRedirect;
            req.UserAgent = "ADFS-Tester/1.0";
            req.Accept = "*/*";
            if (!string.IsNullOrEmpty(soapAction)) req.Headers["SOAPAction"] = "\"" + soapAction + "\"";
            if (!string.IsNullOrEmpty(basicUser))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(basicUser + ":" + (basicPass ?? "")));
                req.Headers["Authorization"] = "Basic " + token;
            }

            try
            {
                if (body != null && body.Length > 0)
                {
                    req.ContentType = contentType;
                    req.ContentLength = body.Length;
                    using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                    Fill(res, resp);
            }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr != null)
                {
                    using (hr) Fill(res, hr);   // 4xx/5xx: Body trotzdem auslesen
                }
                else
                {
                    res.Transport = false; res.Error = we;
                }
            }
            catch (Exception ex)
            {
                res.Transport = false; res.Error = ex;
            }
            return res;
        }

        private static void Fill(HttpResult res, HttpWebResponse resp)
        {
            res.Transport = true;
            res.Status = (int)resp.StatusCode;
            res.StatusText = resp.StatusDescription ?? resp.StatusCode.ToString();
            res.Headers = resp.Headers;
            res.Location = resp.Headers["Location"] ?? "";
            try
            {
                using (var rs = resp.GetResponseStream())
                using (var rd = new StreamReader(rs, Encoding.UTF8))
                    res.Body = rd.ReadToEnd();
            }
            catch { res.Body = ""; }
        }
    }
}
