// ADFS-Test-Tool - Hauptfenster.
// Tabs: Verbindung & Config | Metadata & Zertifikate | WS-Federation |
//       WS-Trust | SAML 2.0 | OIDC/OAuth | Report

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AdfsTester
{
    public sealed class MainForm : Form
    {
        // Config-Felder
        private TextBox txtAdfsHost, txtRealm, txtWreply, txtClientId, txtClientSecret,
                        txtRedirectUri, txtScope, txtResource, txtUsername, txtPassword,
                        txtTimeout, txtCertWarn;
        private CheckBox chkShowSecrets, chkInteractive, chkVerifyCert;
        private ToolTip _tip;

        // Ergebnis-Ansichten
        private readonly Dictionary<string, ResultView> _views = new Dictionary<string, ResultView>();
        private TextBox _reportBox;

        private MenuStrip _menu;
        private StatusStrip _status;
        private ToolStripStatusLabel _lblStatus, _lblSummary;
        private TabControl _tabs;

        // Laufzeit-Status
        private AdfsMetadata _md;
        private readonly List<TestRun> _allRuns = new List<TestRun>();
        private bool _busy;

        private static readonly Color ACCENT = Color.FromArgb(0, 120, 215);
        private static readonly Color LABEL_FORE = Color.FromArgb(60, 60, 60);

        private static readonly string ConfigFile =
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "AdfsTester.config.txt");

        public MainForm()
        {
            Text = "ADFS-Tester - Verbindung & Zertifikate (WS-Fed / WS-Trust / SAML / OAuth / OIDC)";
            ClientSize = new Size(1180, 860);
            MinimumSize = new Size(1000, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5F);
            BackColor = Color.FromArgb(243, 243, 247);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildMenu();
            BuildStatus();
            BuildTabs();
            LoadConfigSafe();
            SetStatus("Bereit", LABEL_FORE);
        }

        // ===================== MENU / STATUS =====================

        private void BuildMenu()
        {
            _menu = new MenuStrip();
            var miFile = new ToolStripMenuItem("&Datei");
            miFile.DropDownItems.Add(Item("Konfiguration &speichern", Keys.Control | Keys.S, (s, e) => SaveConfigSafe()));
            miFile.DropDownItems.Add(Item("Konfiguration &laden", Keys.Control | Keys.O, (s, e) => LoadConfigSafe()));
            miFile.DropDownItems.Add(new ToolStripSeparator());
            miFile.DropDownItems.Add(Item("Report als &TXT exportieren ...", Keys.None, (s, e) => ExportReport(false)));
            miFile.DropDownItems.Add(Item("Report als &HTML exportieren ...", Keys.None, (s, e) => ExportReport(true)));
            miFile.DropDownItems.Add(new ToolStripSeparator());
            miFile.DropDownItems.Add(Item("&Beenden", Keys.Alt | Keys.F4, (s, e) => Close()));

            var miRun = new ToolStripMenuItem("&Test");
            miRun.DropDownItems.Add(Item("Metadata && Zertifikate &laden", Keys.F5, (s, e) => RunMetadata()));
            miRun.DropDownItems.Add(Item("&Alle Protokolle testen", Keys.F6, (s, e) => RunAll()));

            var miHelp = new ToolStripMenuItem("&Hilfe");
            miHelp.DropDownItems.Add(Item("&ADFS-Hinweise ...", Keys.F1, (s, e) => ShowHelp()));
            miHelp.DropDownItems.Add(Item("&Ueber ...", Keys.None, (s, e) => ShowAbout()));

            _menu.Items.Add(miFile);
            _menu.Items.Add(miRun);
            _menu.Items.Add(miHelp);
            MainMenuStrip = _menu;
            Controls.Add(_menu);
        }

        private static ToolStripMenuItem Item(string text, Keys keys, EventHandler h)
        {
            var it = new ToolStripMenuItem(text);
            if (keys != Keys.None) it.ShortcutKeys = keys;
            it.Click += h;
            return it;
        }

        private void BuildStatus()
        {
            _status = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel { Text = "  Bereit", AutoSize = false, Width = 260,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            _lblSummary = new ToolStripStatusLabel { Text = "", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _status.Items.Add(_lblStatus);
            _status.Items.Add(new ToolStripSeparator());
            _status.Items.Add(_lblSummary);
            Controls.Add(_status);
        }

        private void SetStatus(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, color))); return; }
            _lblStatus.Text = "  " + text;
            _lblStatus.ForeColor = color;
        }

        // ===================== TABS =====================

        private void BuildTabs()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };

            _tabs.TabPages.Add(BuildConfigTab());
            _tabs.TabPages.Add(BuildResultTab("Metadata & Zertifikate", "Metadata", false));
            _tabs.TabPages.Add(BuildResultTab("WS-Federation", "WS-Federation", true));
            _tabs.TabPages.Add(BuildResultTab("WS-Trust", "WS-Trust", false));
            _tabs.TabPages.Add(BuildResultTab("SAML 2.0", "SAML 2.0", true));
            _tabs.TabPages.Add(BuildResultTab("OIDC / OAuth", "OIDC / OAuth", true));
            _tabs.TabPages.Add(BuildReportTab());

            Controls.Add(_tabs);
            _tabs.BringToFront();
        }

        private TabPage BuildConfigTab()
        {
            var tab = new TabPage("Verbindung & Config") { BackColor = Color.White };

            _tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 350, ReshowDelay = 80,
                ShowAlways = true, ToolTipTitle = "Wo finde ich das?", IsBalloon = true };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White,
                Padding = new Padding(12, 12, 8, 6), WrapContents = false };
            buttons.Controls.Add(MakeButton("1) Metadata && Zertifikate laden  (F5)", 290, (s, e) => RunMetadata()));
            buttons.Controls.Add(MakeButton("2) Alle Protokolle testen  (F6)", 240, (s, e) => RunAll()));
            chkInteractive = new CheckBox { Text = "Interaktiv (Browser-Login) fuer WS-Fed / SAML / OIDC",
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(18, 9, 0, 0), Cursor = Cursors.Help };
            buttons.Controls.Add(chkInteractive);
            _tip.SetToolTip(chkInteractive,
                "Aus: schneller Test ohne Browser (Erreichbarkeit, Zertifikate, WS-Trust,\n" +
                "OAuth Client-Credentials/ROPC).\n\n" +
                "An: WS-Fed / SAML / OAuth-Code oeffnen den System-Browser zur echten\n" +
                "Anmeldung. Voraussetzung: die Redirect-URI ist in ADFS registriert.");

            // 3-Spalten-Raster: Label | Eingabefeld (waechst mit) | Hinweis
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoScroll = true,
                BackColor = Color.White, Padding = new Padding(12, 10, 12, 10),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

            txtAdfsHost = Row(t, "ADFS-Host / Basis-URL", "z.B. adfs.firma.tld",
                "DNS-Name des ADFS-Dienstes (Federation Service Name).\n\n" +
                "Zu finden in der ADFS-Verwaltung:\n" +
                "AD FS > Aktion 'Verbunddiensteigenschaften bearbeiten' > Feld 'Verbunddienstname'.\n" +
                "Oder die URL, die Benutzer beim Login sehen: https://<host>/adfs/ls\n" +
                "PowerShell: (Get-AdfsProperties).HostName\n\n" +
                "Beispiel: adfs.firma.tld");
            txtRealm = Row(t, "Realm / RP-Identifier", "wtrealm bzw. SAML-Audience (= Trust-Identifier in ADFS)",
                "Eindeutiger Bezeichner der Anwendung (Relying Party).\n\n" +
                "ADFS-Verwaltung > 'Vertrauensstellungen der vertrauenden Seite'\n" +
                "(Relying Party Trusts) > <Anwendung> > Eigenschaften > Reiter 'Bezeichner'.\n" +
                "PowerShell: Get-AdfsRelyingPartyTrust | Select Name,Identifier\n\n" +
                "WS-Fed = wtrealm, SAML = Audience / SP-EntityID.");
            txtWreply = Row(t, "wreply (optional)", "WS-Fed Reply-URL fuer nicht-interaktiv",
                "Optionale WS-Federation Antwort-URL (wohin das Token gesendet wird).\n\n" +
                "ADFS > RP-Trust > Reiter 'Endpunkte' > WS-Federation Passive Endpoint.\n" +
                "Nur fuer den nicht-interaktiven WS-Fed-Test noetig; sonst leer lassen.");
            txtClientId = Row(t, "OAuth ClientId", "registrierte Client-ID",
                "Client-Bezeichner der OAuth/OIDC-Anwendung.\n\n" +
                "ADFS-Verwaltung > 'Anwendungsgruppen' > <Gruppe> > Anwendung\n" +
                "(Server-/Web-/Native App) > Feld 'Clientbezeichner'.\n" +
                "PowerShell: Get-AdfsApplicationGroup / Get-AdfsNativeClientApplication");
            txtClientSecret = Row(t, "OAuth ClientSecret", "(DPAPI-verschluesselt gespeichert)",
                "Geheimnis der OAuth-Anwendung (vertraulicher Client / Server Application).\n\n" +
                "Wird bei der Registrierung in der Anwendungsgruppe EINMALIG angezeigt.\n" +
                "Verloren? In der Anwendungsgruppe > Server Application ein neues generieren.\n" +
                "Native/Public Clients haben KEIN Secret - dann leer lassen.", true);
            txtRedirectUri = Row(t, "Redirect-URI", "muss in ADFS registriert sein; hoher Loopback-Port",
                "Umleitungs-URI (Redirect/Reply URL) der OAuth-Anwendung.\n\n" +
                "ADFS > Anwendungsgruppe > Anwendung > 'Umleitungs-URI'.\n" +
                "Muss EXAKT uebereinstimmen (haeufigste OAuth-Fehlerquelle).\n\n" +
                "Dieses Tool startet einen lokalen Listener auf einem hohen Loopback-Port.\n" +
                "Genau diese URI (z.B. http://localhost:8765/adfs-tester/) in ADFS hinterlegen.");
            txtScope = Row(t, "Scope", "z.B. openid",
                "OAuth/OIDC-Scopes (durch Leerzeichen getrennt).\n\n" +
                "'openid' anfordern, damit ADFS ein id_token ausstellt.\n" +
                "Erlaubte Scopes der Web-App: ADFS > Anwendungsgruppe > Web-API >\n" +
                "Reiter 'Clientberechtigungen'. PowerShell: Get-AdfsScopeDescription");
            txtResource = Row(t, "Resource (optional)", "ADFS 'resource'-Parameter = RP-Identifier",
                "ADFS-spezifischer 'resource'-Parameter = Bezeichner der Ziel-API/Anwendung\n" +
                "(oft identisch mit dem Realm).\n\n" +
                "= 'Bezeichner' der Web-API in der Anwendungsgruppe.\n" +
                "In aelteren ADFS-OAuth-Flows erforderlich, in neueren via Scope ersetzt.");
            txtUsername = Row(t, "Username (WS-Trust/ROPC)", "DOMAIN\\user oder UPN",
                "Testbenutzer fuer nicht-interaktive Flows (WS-Trust / OAuth ROPC).\n\n" +
                "Format: DOMAIN\\benutzer  oder  UPN (benutzer@firma.tld).\n" +
                "Ein gueltiges AD-Konto mit Zugriff auf die Anwendung.\n" +
                "Fuer reine Erreichbarkeits-/Zertifikatstests nicht noetig.");
            txtPassword = Row(t, "Passwort", "(DPAPI-verschluesselt gespeichert)",
                "Passwort des Testbenutzers.\n\n" +
                "Wird nur lokal DPAPI-verschluesselt gespeichert und zum Token-Bezug an\n" +
                "ADFS gesendet (WS-Trust usernamemixed bzw. OAuth ROPC).", true);
            txtTimeout = Row(t, "Timeout (Sek.)", "Standard 30",
                "Maximale Wartezeit pro Netzwerk-/HTTP-Aufruf in Sekunden.\n" +
                "Standard 30. Bei langsamen Verbindungen/Proxies erhoehen.");
            txtCertWarn = Row(t, "Cert-Warnung (Tage)", "Warnung wenn Zertifikat < X Tage gueltig",
                "Schwellwert in Tagen: Zertifikate, die in weniger als dieser Zeit ablaufen,\n" +
                "werden als Warnung (gelb) markiert. Standard 30.\n\n" +
                "Abgelaufene Token-Signing-Zertifikate sind die haeufigste ADFS-Stoerung.");

            chkShowSecrets = new CheckBox { Text = "Secrets anzeigen", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            chkShowSecrets.CheckedChanged += (s, e) =>
            {
                char pc = chkShowSecrets.Checked ? '\0' : '●';
                txtClientSecret.PasswordChar = pc; txtPassword.PasswordChar = pc;
            };
            chkVerifyCert = new CheckBox { Text = "Server-Zertifikat strikt pruefen (aus = nur diagnostisch)",
                AutoSize = true, Checked = true, Margin = new Padding(3, 4, 3, 6), Cursor = Cursors.Help };
            _tip.SetToolTip(chkShowSecrets, "Zeigt ClientSecret und Passwort im Klartext an (sonst maskiert).");
            _tip.SetToolTip(chkVerifyCert,
                "An: HTTP-Aufrufe brechen bei ungueltigem TLS-Zertifikat ab (normaler Modus).\n\n" +
                "Aus: Zertifikatfehler werden bei HTTP-Aufrufen ignoriert, damit Metadata/Token\n" +
                "auch bei Self-Signed-/Testumgebungen geladen werden koennen. Die Zertifikats-\n" +
                "pruefung selbst (Tab 'Metadata & Zertifikate') laeuft unabhaengig davon weiter.");
            AddCheckRow(t, chkShowSecrets);
            AddCheckRow(t, chkVerifyCert);

            tab.Controls.Add(t);
            tab.Controls.Add(buttons);
            return tab;
        }

        // Eine Formularzeile: Label | mitwachsendes Eingabefeld | grauer Hinweis.
        // tooltip beschreibt, WO der Wert in ADFS zu finden ist (an allen 3 Teilen).
        private TextBox Row(TableLayoutPanel t, string label, string hint, string tooltip, bool secret = false)
        {
            var lbl = new Label { Text = "ⓘ " + label, AutoSize = false, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = LABEL_FORE, Margin = new Padding(3, 5, 3, 5),
                Cursor = Cursors.Help };
            t.Controls.Add(lbl);
            var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 5) };
            if (secret) box.PasswordChar = '●';
            t.Controls.Add(box);
            var hintLbl = new Label { Text = hint, AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray, Margin = new Padding(8, 5, 3, 5),
                Cursor = Cursors.Help };
            t.Controls.Add(hintLbl);
            if (!string.IsNullOrEmpty(tooltip) && _tip != null)
            {
                _tip.SetToolTip(lbl, tooltip);
                _tip.SetToolTip(box, tooltip);
                _tip.SetToolTip(hintLbl, tooltip);
            }
            return box;
        }

        private static void AddCheckRow(TableLayoutPanel t, CheckBox cb)
        {
            t.Controls.Add(new Label());   // Spalte 0 leer
            t.Controls.Add(cb);            // Spalte 1: Checkbox
            t.Controls.Add(new Label());   // Spalte 2 leer
        }

        private TabPage BuildResultTab(string title, string key, bool perTabInteractive)
        {
            var tab = new TabPage(title) { BackColor = Color.White };
            var view = new ResultView();
            _views[key] = view;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White,
                Padding = new Padding(10, 9, 0, 0), WrapContents = false };
            if (key == "Metadata")
                top.Controls.Add(MakeButton("Metadata && Zertifikate laden", 230, (s, e) => RunMetadata()));
            else
                top.Controls.Add(MakeButton("Diesen Test ausfuehren", 190, (s, e) => RunSingle(key)));

            tab.Controls.Add(view);
            tab.Controls.Add(top);
            return tab;
        }

        private TabPage BuildReportTab()
        {
            var tab = new TabPage("Report") { BackColor = Color.White };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White,
                Padding = new Padding(10, 9, 0, 0), WrapContents = false };
            top.Controls.Add(MakeButton("Report aktualisieren", 170, (s, e) => RefreshReport()));
            top.Controls.Add(MakeButton("Als TXT exportieren", 170, (s, e) => ExportReport(false)));
            top.Controls.Add(MakeButton("Als HTML exportieren", 170, (s, e) => ExportReport(true)));
            _reportBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(250, 250, 250) };
            tab.Controls.Add(_reportBox);
            tab.Controls.Add(top);
            return tab;
        }

        private Button MakeButton(string text, int width, EventHandler onClick)
        {
            var b = new Button { Text = text, Width = width, Height = 34, AutoSize = false,
                FlatStyle = FlatStyle.Flat, BackColor = ACCENT, ForeColor = Color.White,
                Margin = new Padding(0, 0, 10, 0), Font = new Font("Segoe UI", 9.5F) };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            return b;
        }

        // ===================== EXECUTION =====================

        private AppConfig BuildConfig()
        {
            return new AppConfig
            {
                AdfsHost = txtAdfsHost.Text.Trim(),
                Realm = txtRealm.Text.Trim(),
                Wreply = txtWreply.Text.Trim(),
                ClientId = txtClientId.Text.Trim(),
                ClientSecret = txtClientSecret.Text,
                RedirectUri = txtRedirectUri.Text.Trim(),
                Scope = txtScope.Text.Trim(),
                OAuthResource = txtResource.Text.Trim(),
                Username = txtUsername.Text.Trim(),
                Password = txtPassword.Text,
                TimeoutSeconds = ParseInt(txtTimeout.Text, 30),
                CertWarnDays = ParseInt(txtCertWarn.Text, 30),
                VerifyServerCert = chkVerifyCert.Checked
            };
        }

        private void RunMetadata()
        {
            RunInBackground("Lade Metadata & Zertifikate ...", cfg =>
            {
                var run = new TestRun("Metadata & Zertifikate");
                EndpointCertChecks(run, cfg);
                _md = MetadataClient.Load(run, cfg);
                BeginInvoke(new Action(() => { _views["Metadata"].Populate(run); StoreRun(run); }));
            });
        }

        private void RunAll()
        {
            RunInBackground("Teste alle Protokolle ...", cfg =>
            {
                _allRuns.Clear();
                var meta = new TestRun("Metadata & Zertifikate");
                EndpointCertChecks(meta, cfg);
                _md = MetadataClient.Load(meta, cfg);
                bool interactive = GetInteractive();

                var wsfed = WsFedTester.Run(cfg, _md, interactive);
                var wstrust = WsTrustTester.Run(cfg, _md);
                var saml = SamlTester.Run(cfg, _md, interactive);
                var oidc = OidcTester.Run(cfg, _md, interactive);

                BeginInvoke(new Action(() =>
                {
                    _views["Metadata"].Populate(meta);
                    _views["WS-Federation"].Populate(wsfed);
                    _views["WS-Trust"].Populate(wstrust);
                    _views["SAML 2.0"].Populate(saml);
                    _views["OIDC / OAuth"].Populate(oidc);
                    _allRuns.Add(meta); _allRuns.Add(wsfed); _allRuns.Add(wstrust); _allRuns.Add(saml); _allRuns.Add(oidc);
                    RefreshReport();
                }));
            });
        }

        private void RunSingle(string key)
        {
            RunInBackground("Teste " + key + " ...", cfg =>
            {
                if (_md == null)
                {
                    var meta = new TestRun("Metadata & Zertifikate");
                    EndpointCertChecks(meta, cfg);
                    _md = MetadataClient.Load(meta, cfg);
                    BeginInvoke(new Action(() => { _views["Metadata"].Populate(meta); StoreRun(meta); }));
                }
                bool interactive = GetInteractive();
                TestRun run;
                switch (key)
                {
                    case "WS-Federation": run = WsFedTester.Run(cfg, _md, interactive); break;
                    case "WS-Trust": run = WsTrustTester.Run(cfg, _md); break;
                    case "SAML 2.0": run = SamlTester.Run(cfg, _md, interactive); break;
                    case "OIDC / OAuth": run = OidcTester.Run(cfg, _md, interactive); break;
                    default: return;
                }
                BeginInvoke(new Action(() => { _views[key].Populate(run); StoreRun(run); }));
            });
        }

        // TLS-/Zertifikatspruefung des ADFS-Hosts (Teil von Metadata-Tab)
        private void EndpointCertChecks(TestRun run, AppConfig cfg)
        {
            try
            {
                var uri = new Uri(cfg.BaseUrl);
                int port = uri.Port > 0 ? uri.Port : 443;
                CertificateInspector.InspectEndpoint(run, uri.Host, port, cfg.CertWarnDays, cfg.TimeoutSeconds);
            }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("ADFS-Host", "URL '" + cfg.AdfsHost + "'", ex));
            }
        }

        private bool GetInteractive()
        {
            bool v = false;
            if (InvokeRequired) Invoke(new Action(() => v = chkInteractive.Checked));
            else v = chkInteractive.Checked;
            return v;
        }

        private void RunInBackground(string statusText, Action<AppConfig> work)
        {
            if (_busy) { MessageBox.Show("Es laeuft bereits ein Test.", "Bitte warten", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            AppConfig cfg = BuildConfig();
            if (string.IsNullOrEmpty(cfg.AdfsHost))
            { MessageBox.Show("Bitte ADFS-Host angeben.", "Eingabe fehlt", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            // 'strikt pruefen' aus -> Zertifikatfehler bei HTTP-Aufrufen ignorieren (nur Diagnose).
            System.Net.ServicePointManager.ServerCertificateValidationCallback =
                cfg.VerifyServerCert ? (System.Net.Security.RemoteCertificateValidationCallback)null
                                     : ((s, c, ch, e) => true);

            _busy = true;
            SetStatus(statusText, ACCENT);
            UseWaitCursor = true;

            var th = new Thread(() =>
            {
                try { work(cfg); SetStatus("Fertig", Color.FromArgb(16, 124, 16)); }
                catch (Exception ex)
                {
                    SetStatus("Fehler", Color.FromArgb(196, 43, 28));
                    BeginInvoke(new Action(() => MessageBox.Show(ex.ToString(), "Unerwarteter Fehler",
                        MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                finally { BeginInvoke(new Action(() => { _busy = false; UseWaitCursor = false; UpdateSummary(); })); }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        private void StoreRun(TestRun run)
        {
            for (int i = _allRuns.Count - 1; i >= 0; i--)
                if (_allRuns[i].Title == run.Title) _allRuns.RemoveAt(i);
            _allRuns.Add(run);
            RefreshReport();
        }

        private void UpdateSummary()
        {
            int ok = 0, info = 0, warn = 0, err = 0;
            foreach (var r in _allRuns)
            { ok += r.CountOf(Severity.Ok); info += r.CountOf(Severity.Info); warn += r.CountOf(Severity.Warning); err += r.CountOf(Severity.Error); }
            _lblSummary.Text = string.Format("OK: {0}   Info: {1}   Warnungen: {2}   Fehler: {3}", ok, info, warn, err);
            _lblSummary.ForeColor = err > 0 ? Color.FromArgb(196, 43, 28)
                : (warn > 0 ? Color.FromArgb(183, 121, 31) : Color.FromArgb(16, 124, 16));
        }

        // ===================== REPORT =====================

        private void RefreshReport()
        {
            if (_reportBox == null) return;
            _reportBox.Text = ReportBuilder.BuildText(_allRuns, BuildConfig(), Timestamp());
            UpdateSummary();
        }

        private void ExportReport(bool html)
        {
            if (_allRuns.Count == 0) { MessageBox.Show("Noch keine Ergebnisse. Bitte zuerst einen Test ausfuehren.", "Kein Report", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (var dlg = new SaveFileDialog
            {
                Filter = html ? "HTML-Datei (*.html)|*.html" : "Textdatei (*.txt)|*.txt",
                FileName = "ADFS-Tester-Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (html ? ".html" : ".txt")
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var cfg = BuildConfig();
                var content = html ? ReportBuilder.BuildHtml(_allRuns, cfg, Timestamp())
                                   : ReportBuilder.BuildText(_allRuns, cfg, Timestamp());
                try { File.WriteAllText(dlg.FileName, content, System.Text.Encoding.UTF8);
                    SetStatus("Report exportiert: " + dlg.FileName, Color.FromArgb(16, 124, 16)); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Export-Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private static string Timestamp() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

        // ===================== CONFIG IO =====================

        private void LoadConfigSafe()
        {
            try
            {
                var c = AppConfig.Load(ConfigFile);
                txtAdfsHost.Text = c.AdfsHost; txtRealm.Text = c.Realm; txtWreply.Text = c.Wreply;
                txtClientId.Text = c.ClientId; txtClientSecret.Text = c.ClientSecret;
                txtRedirectUri.Text = c.RedirectUri; txtScope.Text = c.Scope; txtResource.Text = c.OAuthResource;
                txtUsername.Text = c.Username; txtPassword.Text = c.Password;
                txtTimeout.Text = c.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
                txtCertWarn.Text = c.CertWarnDays.ToString(CultureInfo.InvariantCulture);
                chkVerifyCert.Checked = c.VerifyServerCert;
            }
            catch (Exception ex) { SetStatus("Config-Laden fehlgeschlagen: " + ex.Message, Color.FromArgb(196, 43, 28)); }
        }

        private void SaveConfigSafe()
        {
            try { BuildConfig().Save(ConfigFile); SetStatus("Konfiguration gespeichert.", Color.FromArgb(16, 124, 16)); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Speichern fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static int ParseInt(string s, int def)
        { int r; return int.TryParse((s ?? "").Trim(), out r) ? r : def; }

        // ===================== HELP =====================

        private void ShowHelp()
        {
            MessageBox.Show(
                "ADFS-Tester - Kurzanleitung\r\n\r\n" +
                "1) ADFS-Host eintragen (z.B. adfs.firma.tld) und 'Metadata & Zertifikate laden'.\r\n" +
                "   -> prueft TLS/SSL, Zertifikatskette, Token-Signing-/Encryption-Zertifikate.\r\n\r\n" +
                "2) Fuer Protokoll-Tests Realm/ClientId/Credentials ausfuellen.\r\n\r\n" +
                "Nicht-interaktiv (ohne Browser):\r\n" +
                "  - WS-Trust: Username + Passwort -> SAML-Token\r\n" +
                "  - OAuth Client-Credentials: ClientId + ClientSecret\r\n" +
                "  - OAuth ROPC: ClientId + Username + Passwort\r\n\r\n" +
                "Interaktiv (Checkbox 'Interaktiv'):\r\n" +
                "  - WS-Fed / SAML / OAuth-Code oeffnen den System-Browser.\r\n" +
                "  - Die Redirect-URI muss EXAKT in ADFS registriert sein\r\n" +
                "    (hoher Loopback-Port, z.B. http://localhost:8765/adfs-tester/).\r\n\r\n" +
                "Report-Tab: Gesamtuebersicht, Export als TXT/HTML fuer Support-Tickets.",
                "Hilfe", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "ADFS-Tester 1.0\r\n\r\n" +
                "Diagnose von ADFS-Verbindungen und -Zertifikaten.\r\n" +
                "Protokolle: WS-Federation, WS-Trust, SAML 2.0, OAuth 2.0, OpenID Connect.\r\n\r\n" +
                ".NET Framework 4.5+ / WinForms, ohne externe Bibliotheken.",
                "Ueber ADFS-Tester", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
