// OAuth 2.0 / OpenID Connect. Nicht-interaktiv: Client-Credentials und ROPC.
// Interaktiv: Authorization-Code-Flow. JWT-Tokens werden gegen die JWKS-Keys
// verifiziert (Signatur + iss/aud/exp/nbf).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace AdfsTester
{
    public static class OidcTester
    {
        public static TestRun Run(AppConfig cfg, AdfsMetadata md, TestDepth depth)
        {
            bool deep = depth == TestDepth.Deep;
            var run = new TestRun("OIDC / OAuth");

            if (!md.OpenIdLoaded)
            {
                run.Warn("Discovery", "OpenID-Discovery wurde nicht geladen - OAuth/OIDC evtl. nicht aktiv.",
                         "Tab 'Metadata & Zertifikate' pruefen. ADFS 2012R2 hat eingeschraenkten OAuth-Support.");
                return run;
            }

            string tokenEp = !string.IsNullOrEmpty(md.TokenEndpoint) ? md.TokenEndpoint : cfg.OAuthTokenEndpoint;
            string authEp = !string.IsNullOrEmpty(md.AuthorizeEndpoint) ? md.AuthorizeEndpoint : cfg.OAuthAuthorizeEndpoint;

            // Endpunkt-/Discovery-Praesenz (gilt fuer beide Modi)
            string epDetail = "authorize: " + (md.AuthorizeEndpoint.Length > 0) +
                              ", token: " + (md.TokenEndpoint.Length > 0) + ", jwks-keys: " + md.JwksKeys.Count + ".";
            if (md.AuthorizeEndpoint.Length > 0 && md.TokenEndpoint.Length > 0)
                run.Ok("Endpunkte", epDetail);
            else
                run.Warn("Endpunkte", epDetail,
                         "authorize_endpoint und/oder token_endpoint fehlen in der Discovery - OAuth/OIDC ist unvollstaendig konfiguriert.");

            if (!deep)
            {
                run.Info("Schnelltest", "OAuth/OIDC-Discovery und Endpunkte geprueft. " +
                         "Fuer echten Token-Bezug (Client-Credentials/ROPC/Authorization-Code) den 'Tiefen Test' verwenden.");
                return run;
            }

            // ---- Tiefer Test ----
            if (string.IsNullOrEmpty(cfg.ClientId))
                run.Warn("ClientId", "Keine ClientId konfiguriert - OAuth-Flows benoetigen eine registrierte Client-ID.");

            bool ranNonInteractive = false;
            if (!string.IsNullOrEmpty(cfg.ClientId) && !string.IsNullOrEmpty(cfg.ClientSecret))
            { ClientCredentials(run, cfg, md, tokenEp); ranNonInteractive = true; }
            else
                run.Info("Client-Credentials", "Uebersprungen (kein ClientId/ClientSecret).");

            if (!string.IsNullOrEmpty(cfg.Username) && !string.IsNullOrEmpty(cfg.Password) && !string.IsNullOrEmpty(cfg.ClientId))
            { Ropc(run, cfg, md, tokenEp); ranNonInteractive = true; }
            else
                run.Info("ROPC", "Uebersprungen (Username/Passwort/ClientId noetig).");

            // Browser-Code-Flow nur, wenn kein nicht-interaktiver Grant moeglich war
            // (verhindert unnoetige Browser-Popups bei reinen Confidential-Clients).
            if (string.IsNullOrEmpty(cfg.ClientId))
                run.Info("Authorization-Code", "Uebersprungen (keine ClientId).");
            else if (ranNonInteractive)
                run.Info("Authorization-Code", "Uebersprungen - ein nicht-interaktiver Flow (Client-Credentials/ROPC) wurde bereits getestet. " +
                         "Fuer den interaktiven Browser-Code-Flow ClientSecret/Zugangsdaten leeren.");
            else
                AuthorizationCode(run, cfg, md, authEp, tokenEp);
            return run;
        }

        private static void ClientCredentials(TestRun run, AppConfig cfg, AdfsMetadata md, string tokenEp)
        {
            var form = new Dictionary<string, string>
            {
                {"grant_type", "client_credentials"},
                {"client_id", cfg.ClientId},
                {"client_secret", cfg.ClientSecret},
            };
            AddScopeResource(form, cfg);
            var r = HttpHelper.PostForm(tokenEp, form, cfg.TimeoutSeconds);
            HandleTokenResponse(run, cfg, md, r, "Client-Credentials", false);
        }

        private static void Ropc(TestRun run, AppConfig cfg, AdfsMetadata md, string tokenEp)
        {
            var form = new Dictionary<string, string>
            {
                {"grant_type", "password"},
                {"client_id", cfg.ClientId},
                {"username", cfg.Username},
                {"password", cfg.Password},
            };
            if (!string.IsNullOrEmpty(cfg.ClientSecret)) form["client_secret"] = cfg.ClientSecret;
            AddScopeResource(form, cfg);
            var r = HttpHelper.PostForm(tokenEp, form, cfg.TimeoutSeconds);
            HandleTokenResponse(run, cfg, md, r, "ROPC (Passwort)", true);
        }

        private static void AuthorizationCode(TestRun run, AppConfig cfg, AdfsMetadata md, string authEp, string tokenEp)
        {
            string state = Guid.NewGuid().ToString("N");
            string nonce = Guid.NewGuid().ToString("N");
            var url = authEp + (authEp.Contains("?") ? "&" : "?") +
                      "response_type=code" +
                      "&client_id=" + Uri.EscapeDataString(cfg.ClientId ?? "") +
                      "&redirect_uri=" + Uri.EscapeDataString(cfg.RedirectUri ?? "") +
                      "&scope=" + Uri.EscapeDataString(string.IsNullOrEmpty(cfg.Scope) ? "openid" : cfg.Scope) +
                      "&state=" + state + "&nonce=" + nonce;
            if (!string.IsNullOrEmpty(cfg.OAuthResource))
                url += "&resource=" + Uri.EscapeDataString(cfg.OAuthResource);

            run.Info("Authorize-URL", ErrorLogger.Truncate(url, 1000));
            run.Info("Interaktiv", "System-Browser wird geoeffnet. Bitte am ADFS anmelden ...");

            var cap = BrowserFlow.Capture(cfg.RedirectUri, url, Math.Max(cfg.TimeoutSeconds, 120));
            if (!cap.Success)
            {
                run.Error("Authorization-Code Login", cap.Error,
                          "redirect_uri muss EXAKT der in ADFS registrierten URI entsprechen (haeufigste OAuth-Fehlerquelle).", cap.RawRequest);
                return;
            }

            string err;
            if (cap.Params.TryGetValue("error", out err))
            {
                string desc; cap.Params.TryGetValue("error_description", out desc);
                run.Error("Authorization-Code", "ADFS meldet: " + err + (desc != null ? " - " + desc : ""),
                          InterpretOAuthError(err), cap.RawRequest);
                return;
            }

            string code;
            if (!cap.Params.TryGetValue("code", out code) || string.IsNullOrEmpty(code))
            {
                run.Error("Authorization-Code", "Kein 'code' im Redirect.",
                          "Parameter: " + cap.KeysCsv(), cap.RawRequest);
                return;
            }
            run.Ok("Authorization-Code", "Code empfangen - tausche gegen Token ...");

            string returnedState; cap.Params.TryGetValue("state", out returnedState);
            if (!string.IsNullOrEmpty(returnedState) && returnedState != state)
                run.Warn("State", "state stimmt nicht ueberein (CSRF-Schutz).", "Moegliche Manipulation oder paralleler Login.");

            var form = new Dictionary<string, string>
            {
                {"grant_type", "authorization_code"},
                {"code", code},
                {"redirect_uri", cfg.RedirectUri},
                {"client_id", cfg.ClientId},
            };
            if (!string.IsNullOrEmpty(cfg.ClientSecret)) form["client_secret"] = cfg.ClientSecret;
            var r = HttpHelper.PostForm(tokenEp, form, cfg.TimeoutSeconds);
            HandleTokenResponse(run, cfg, md, r, "Authorization-Code", true);
        }

        private static void AddScopeResource(Dictionary<string, string> form, AppConfig cfg)
        {
            if (!string.IsNullOrEmpty(cfg.Scope)) form["scope"] = cfg.Scope;
            if (!string.IsNullOrEmpty(cfg.OAuthResource)) form["resource"] = cfg.OAuthResource;
        }

        private static void HandleTokenResponse(TestRun run, AppConfig cfg, AdfsMetadata md, HttpResult r, string label, bool expectIdToken)
        {
            if (!r.Transport)
            {
                run.Add(ErrorLogger.ToCheckResult(label, "Token-Request", r.Error));
                return;
            }
            if (r.Status != 200)
            {
                string oauthErr = "";
                try { oauthErr = Json.Str(Json.Parse(r.Body), "error"); } catch { }
                run.Error(label, "HTTP " + r.Status + (oauthErr.Length > 0 ? " - " + oauthErr : ""),
                          InterpretOAuthError(oauthErr), ErrorLogger.Truncate(r.Body, 2500));
                return;
            }

            Dictionary<string, object> tok;
            try { tok = Json.Parse(r.Body); }
            catch (Exception ex) { run.Add(ErrorLogger.ToCheckResult(label, "JSON-Parse", ex)); return; }

            run.Ok(label, "Token-Response erhalten (HTTP 200).", Json.Pretty(tok));

            string accessToken = Json.Str(tok, "access_token");
            string idToken = Json.Str(tok, "id_token");

            if (expectIdToken && string.IsNullOrEmpty(idToken))
                run.Warn(label + " id_token", "Kein id_token in der Antwort.",
                         "Scope 'openid' anfordern, damit ADFS ein id_token ausstellt.");

            if (!string.IsNullOrEmpty(idToken))
                ValidateJwt(run, cfg, md, idToken, label + " id_token", cfg.ClientId);
            if (!string.IsNullOrEmpty(accessToken))
                ValidateJwt(run, cfg, md, accessToken, label + " access_token", cfg.OAuthResource);
        }

        private static void ValidateJwt(TestRun run, AppConfig cfg, AdfsMetadata md, string jwt, string label, string expectedAud)
        {
            JwtParts p;
            try { p = JwtHelper.Decode(jwt); }
            catch (Exception ex)
            {
                run.Warn(label, "Token ist kein lesbares JWT: " + ex.Message,
                         "Bei access_token ist ein opakes (nicht-JWT) Format moeglich - dann nicht pruefbar.");
                return;
            }

            run.Info(label + " Inhalt", "alg=" + p.Alg + ", kid=" + p.Kid + ".", "Header:\n" + p.RawHeaderJson + "\n\nPayload:\n" + p.RawPayloadJson);

            if (string.Equals(p.Alg, "none", StringComparison.OrdinalIgnoreCase))
            {
                run.Error(label + " Signatur", "alg=none - Token ist NICHT signiert.",
                          "Ein unsigniertes Token darf nie akzeptiert werden (Algorithm-Confusion-Angriff).");
            }
            else
            {
                var key = FindKey(md, p.Kid);
                if (key == null)
                    run.Error(label + " Signatur", "Kein JWKS-Key mit kid='" + p.Kid + "' gefunden.",
                              "Signing-Key rotiert? JWKS neu laden. kid muss in /discovery/keys vorhanden sein.");
                else
                {
                    var cert = key.ToCertificate();
                    if (cert == null)
                        run.Warn(label + " Signatur", "JWKS-Key ohne x5c-Zertifikat - Verifikation via n/e nicht implementiert fuer dieses Format.",
                                 "ADFS liefert i.d.R. x5c; falls nicht, JWKS-Format pruefen.");
                    else
                    {
                        string detail; bool ok = JwtHelper.VerifyRsa(p, cert, out detail);
                        if (ok) run.Ok(label + " Signatur", detail + " (JWKS kid=" + p.Kid + ").");
                        else run.Error(label + " Signatur", detail,
                                       "Signatur passt nicht zum JWKS-Key - Token manipuliert oder falscher Key.");
                    }
                }
            }

            // Claims pruefen
            CheckClaim(run, label, p, "iss", md.Issuer, true);
            CheckExp(run, label, p);
            CheckAud(run, label, p, expectedAud);
        }

        private static JwksKey FindKey(AdfsMetadata md, string kid)
        {
            if (md.JwksKeys.Count == 0) return null;
            if (string.IsNullOrEmpty(kid)) return md.JwksKeys.Count == 1 ? md.JwksKeys[0] : null;
            foreach (var k in md.JwksKeys) if (k.Kid == kid) return k;
            return null;
        }

        private static void CheckClaim(TestRun run, string label, JwtParts p, string claim, string expected, bool strict)
        {
            string val = Json.Str(p.Payload, claim);
            if (string.IsNullOrEmpty(expected)) { if (val.Length > 0) run.Info(label + " " + claim, val); return; }
            if (string.Equals(val.TrimEnd('/'), expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                run.Ok(label + " " + claim, claim + " stimmt: " + val);
            else
                run.Error(label + " " + claim, claim + "-Mismatch. Token: '" + val + "', erwartet (issuer): '" + expected + "'.",
                          "Oft http/https- oder Trailing-Slash-Unterschied. issuer aus der Discovery muss exakt passen.");
        }

        private static void CheckAud(TestRun run, string label, JwtParts p, string expectedAud)
        {
            string aud = Json.Str(p.Payload, "aud");
            if (string.IsNullOrEmpty(aud)) { run.Warn(label + " aud", "Kein aud-Claim im Token."); return; }
            if (string.IsNullOrEmpty(expectedAud)) { run.Info(label + " aud", aud); return; }
            if (aud.IndexOf(expectedAud, StringComparison.OrdinalIgnoreCase) >= 0)
                run.Ok(label + " aud", "aud passt: " + aud);
            else
                run.Warn(label + " aud", "aud='" + aud + "', erwartet '" + expectedAud + "'.",
                         "aud muss die Client-ID (id_token) bzw. den Resource-Identifier (access_token) enthalten.");
        }

        private static void CheckExp(TestRun run, string label, JwtParts p)
        {
            object expObj;
            if (!p.Payload.TryGetValue("exp", out expObj)) { run.Warn(label + " exp", "Kein exp-Claim."); return; }
            long exp;
            if (!long.TryParse(expObj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out exp))
            { run.Warn(label + " exp", "exp nicht lesbar: " + expObj); return; }
            var expUtc = Epoch.AddSeconds(exp);
            var now = DateTime.UtcNow;
            if (now > expUtc.AddMinutes(5))
                run.Error(label + " exp", "Token abgelaufen am " + expUtc.ToString("u") + " (jetzt " + now.ToString("u") + ").",
                          "Token-Lifetime zu kurz oder Clock-Skew zwischen Client und ADFS.");
            else
                run.Ok(label + " exp", "Gueltig bis " + expUtc.ToString("u") + ".");

            object nbfObj;
            if (p.Payload.TryGetValue("nbf", out nbfObj))
            {
                long nbf;
                if (long.TryParse(nbfObj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out nbf))
                {
                    var nbfUtc = Epoch.AddSeconds(nbf);
                    if (now < nbfUtc.AddMinutes(-5))
                        run.Error(label + " nbf", "Token noch nicht gueltig (nbf " + nbfUtc.ToString("u") + ").",
                                  "Clock-Skew: Systemuhrzeit Client/ADFS synchronisieren.");
                }
            }
        }

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static string InterpretOAuthError(string err)
        {
            if (string.IsNullOrEmpty(err)) return "Token-Endpoint-Fehler - Body in den Rohdaten pruefen.";
            switch (err.ToLowerInvariant())
            {
                case "invalid_client": return "client_id unbekannt oder client_secret falsch. Client-Registrierung in ADFS pruefen.";
                case "invalid_grant": return "Ungueltiger Grant: falsche Credentials (ROPC), abgelaufener/verbrauchter Code, oder redirect_uri-Mismatch.";
                case "invalid_scope": return "Angeforderter Scope ist nicht erlaubt/unbekannt.";
                case "unauthorized_client": return "Client darf diesen Grant-Type nicht verwenden (z.B. ROPC/Client-Credentials nicht erlaubt).";
                case "unsupported_grant_type": return "Grant-Type wird vom Endpoint nicht unterstuetzt.";
                case "access_denied": return "Zugriff verweigert - Consent abgelehnt, MFA/Conditional Access oder Policy.";
                case "server_error": return "Serverseitiger Fehler in ADFS - ADFS-Eventlog (AD FS/Admin) pruefen.";
                default: return "OAuth-Fehler '" + err + "' - ADFS-Eventlog und Client-Konfiguration pruefen.";
            }
        }
    }
}
