// Prueft TLS/SSL-Verbindung und Zertifikate eines Endpoints sowie die
// Token-Signing-/Encryption-Zertifikate aus den Metadaten.

using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace AdfsTester
{
    public static class CertificateInspector
    {
        // Komplette TLS-/Zertifikatspruefung fuer host:port.
        public static void InspectEndpoint(TestRun run, string host, int port, int warnDays, int timeoutSec)
        {
            X509Certificate2 serverCert = null;
            SslPolicyErrors policyErrors = SslPolicyErrors.None;
            X509Chain handshakeChain = null;

            try
            {
                using (var tcp = new TcpClient())
                {
                    var ar = tcp.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(Math.Max(1, timeoutSec) * 1000))
                    {
                        run.Error("TCP-Verbindung " + host + ":" + port,
                                  "Timeout beim Verbindungsaufbau.",
                                  "Port " + port + " erreichbar? Firewall/Proxy? DNS korrekt?");
                        return;
                    }
                    tcp.EndConnect(ar);
                    run.Ok("TCP-Verbindung " + host + ":" + port, "Port offen, TCP-Handshake ok.");

                    RemoteCertificateValidationCallback cb = (s, cert, chain, errors) =>
                    {
                        if (cert != null) serverCert = new X509Certificate2(cert);
                        policyErrors = errors;
                        handshakeChain = chain;
                        return true; // immer akzeptieren, wir bewerten selbst
                    };

                    using (var ssl = new SslStream(tcp.GetStream(), false, cb))
                    {
                        ssl.AuthenticateAsClient(host,
                            null,
                            SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls,
                            false);

                        run.Ok("TLS-Handshake",
                                "Protokoll " + ssl.SslProtocol + ", Cipher " + ssl.CipherAlgorithm +
                                " " + ssl.CipherStrength + " Bit, Hash " + ssl.HashAlgorithm + ".");

                        if (ssl.SslProtocol == SslProtocols.Tls || ssl.SslProtocol == SslProtocols.Tls11)
                            run.Warn("TLS-Version",
                                     "Verbindung nutzt veraltetes " + ssl.SslProtocol + ".",
                                     "TLS 1.2 auf dem ADFS/Reverse-Proxy aktivieren. (TLS 1.3 wird von .NET Framework nicht unterstuetzt - kein Fehler.)");
                    }
                }
            }
            catch (AuthenticationException ex)
            {
                run.Add(ErrorLogger.ToCheckResult("TLS-Handshake", "TLS-Authentifizierung " + host + ":" + port, ex));
                if (serverCert == null) return;
            }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("TLS-Verbindung", "Verbindung " + host + ":" + port, ex));
                return;
            }

            if (serverCert == null)
            {
                run.Warn("Server-Zertifikat", "Kein Zertifikat vom Server erhalten.");
                return;
            }

            // ---- Zertifikat bewerten ----
            run.Info("Server-Zertifikat " + host, "Subject/Issuer/Gueltigkeit siehe Rohdaten.",
                     CertFormat.Describe(serverCert));

            // Hostname-Match
            if ((policyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                run.Error("Hostname-Match",
                          "Zertifikat ist NICHT auf '" + host + "' ausgestellt (CN/SAN passt nicht).",
                          "SAN/CN des Zertifikats muss den ADFS-Hostnamen enthalten.");
            else
                run.Ok("Hostname-Match", "Hostname '" + host + "' im Zertifikat (CN/SAN) enthalten.");

            // Gueltigkeit
            CheckExpiry(run, "Server-Zertifikat Gueltigkeit", serverCert, warnDays);

            // Kette
            CheckChain(run, serverCert, policyErrors);
        }

        // Prueft ein Token-Zertifikat aus den Metadaten (Signing/Encryption).
        public static void InspectTokenCert(TestRun run, string label, X509Certificate2 cert, int warnDays)
        {
            if (cert == null) { run.Warn(label, "Kein Zertifikat vorhanden."); return; }
            run.Info(label, "Thumbprint " + cert.Thumbprint + ".", CertFormat.Describe(cert));
            CheckExpiry(run, label + " Gueltigkeit", cert, warnDays);
        }

        private static void CheckExpiry(TestRun run, string step, X509Certificate2 cert, int warnDays)
        {
            var now = DateTime.Now;
            if (now > cert.NotAfter)
                run.Error(step, "Zertifikat ABGELAUFEN am " + cert.NotAfter.ToString("yyyy-MM-dd") + ".",
                          "Neues Zertifikat einspielen. Bei Token-Signing: AutoCertificateRollover pruefen und RP-Metadaten aktualisieren.");
            else if (now < cert.NotBefore)
                run.Error(step, "Zertifikat noch NICHT gueltig (ab " + cert.NotBefore.ToString("yyyy-MM-dd") + ").",
                          "Systemuhrzeit von Client und ADFS pruefen (Clock-Skew).");
            else
            {
                var daysLeft = (cert.NotAfter - now).TotalDays;
                if (daysLeft <= warnDays)
                    run.Warn(step, "Zertifikat laeuft in " + (int)daysLeft + " Tagen ab (" + cert.NotAfter.ToString("yyyy-MM-dd") + ").",
                             "Rechtzeitig erneuern - abgelaufene Token-Signing-Zertifikate sind die haeufigste ADFS-Stoerung.");
                else
                    run.Ok(step, "Gueltig bis " + cert.NotAfter.ToString("yyyy-MM-dd") + " (" + (int)daysLeft + " Tage).");
            }
        }

        private static void CheckChain(TestRun run, X509Certificate2 cert, SslPolicyErrors policyErrors)
        {
            try
            {
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(15);
                    bool valid = chain.Build(cert);

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Kettenlaenge: " + chain.ChainElements.Count + " Element(e)");
                    foreach (X509ChainElement el in chain.ChainElements)
                    {
                        sb.AppendLine("  - " + el.Certificate.Subject);
                        foreach (var st in el.ChainElementStatus)
                            sb.AppendLine("      [" + st.Status + "] " + st.StatusInformation.Trim());
                    }
                    string raw = sb.ToString();

                    if (valid)
                    {
                        run.Ok("Zertifikatskette", "Kette vollstaendig und vertrauenswuerdig.", raw);
                        return;
                    }

                    // Konkrete Ketten-Fehler herausziehen
                    bool reported = false;
                    foreach (X509ChainStatus st in chain.ChainStatus)
                    {
                        reported = true;
                        switch (st.Status)
                        {
                            case X509ChainStatusFlags.UntrustedRoot:
                            case X509ChainStatusFlags.PartialChain:
                                run.Error("Zertifikatskette",
                                          "Kette nicht vertrauenswuerdig: " + st.Status + ".",
                                          "Self-Signed oder fehlendes Zwischen-/Root-Zertifikat. Root-CA in den vertrauenswuerdigen Stammspeicher importieren.",
                                          raw);
                                break;
                            case X509ChainStatusFlags.Revoked:
                                run.Error("Zertifikatskette", "Zertifikat ist REVOZIERT (CRL/OCSP).",
                                          "Neues, gueltiges Zertifikat einspielen.", raw);
                                break;
                            case X509ChainStatusFlags.RevocationStatusUnknown:
                            case X509ChainStatusFlags.OfflineRevocation:
                                run.Warn("Zertifikatskette - Revocation",
                                         "Sperrstatus konnte nicht geprueft werden (" + st.Status + ").",
                                         "CRL-/OCSP-Endpunkt vom Client erreichbar? Bei isolierten Servern oft unkritisch.",
                                         raw);
                                break;
                            case X509ChainStatusFlags.NotTimeValid:
                                run.Error("Zertifikatskette", "Ein Kettenglied ist abgelaufen/nicht gueltig.",
                                          "Zertifikate erneuern.", raw);
                                break;
                            default:
                                run.Warn("Zertifikatskette", "Status: " + st.Status + " - " + st.StatusInformation.Trim(),
                                         null, raw);
                                break;
                        }
                    }
                    if (!reported)
                        run.Warn("Zertifikatskette", "Kette konnte nicht vollstaendig validiert werden.", null, raw);
                }
            }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("Zertifikatskette", "Kettenpruefung", ex));
            }
        }
    }
}
