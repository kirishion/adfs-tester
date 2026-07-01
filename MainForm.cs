// ADFS-Test-Tool - Hauptfenster.
// Jeder Protokoll-Tab enthaelt genau seine eigenen Eingabefelder. Der Tab
// "Verbindung" haelt nur Gemeinsames (ADFS-Host, Callback-URL, Testbenutzer,
// Optionen).

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
        // Gemeinsam
        private TextBox txtAdfsHost, txtRedirectUri, txtUsername, txtPassword, txtTimeout, txtCertWarn;
        private CheckBox chkShowSecrets, chkVerifyCert;
        // WS-Federation
        private TextBox txtWsFedRealm, txtWsFedReply;
        // WS-Trust
        private TextBox txtWsTrustAppliesTo;
        // SAML
        private TextBox txtSamlRp, txtSamlSignStore, txtSamlSignThumb, txtSamlDecStore, txtSamlDecThumb, txtSamlClaim;
        private CheckBox chkSamlSign;
        private ComboBox cmbSamlSignLoc, cmbSamlDecLoc;
        // OAuth / OIDC
        private TextBox txtClientId, txtClientSecret, txtScope, txtResource;

        private readonly Dictionary<string, ResultView> _views = new Dictionary<string, ResultView>();
        private TextBox _reportBox;
        private ToolTip _tip;      // Feld-Tooltips ("Wo finde ich das?")
        private ToolTip _tipBtn;   // Modus-Tooltips (Schnell/Tief)

        private const string QuickTip =
            "Ohne Browser und ohne Zugangsdaten.\n\n" +
            "Prueft nur Erreichbarkeit, Endpunkte, Metadaten/Zertifikate und ob die noetige\n" +
            "Konfiguration da ist. Fehlende protokollspezifische Felder sind nur Hinweise.";
        private const string DeepTip =
            "End-to-End.\n\n" +
            "Fuehrt die echte Anmeldung durch (System-Browser bzw. Zugangsdaten), holt ein\n" +
            "Token und prueft Signatur/Claims (SAML: inkl. Signieren/Entschluesseln).\n" +
            "Benoetigt die protokollspezifischen Felder dieses Tabs.";

        private MenuStrip _menu;
        private StatusStrip _status;
        private ToolStripStatusLabel _lblStatus, _lblSummary;
        private TabControl _tabs;

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

            _tip = MakeTip("Wo finde ich das?");
            _tipBtn = MakeTip("Testmodus");

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
            miRun.DropDownItems.Add(Item("&Schnelltest (alle Protokolle)", Keys.F6, (s, e) => RunAll(TestDepth.Quick)));
            miRun.DropDownItems.Add(Item("&Tiefer Test (alle Protokolle)", Keys.F7, (s, e) => RunAll(TestDepth.Deep)));

            var miHelp = new ToolStripMenuItem("&Hilfe");
            miHelp.DropDownItems.Add(Item("&ADFS-Hinweise ...", Keys.F1, (s, e) => ShowHelp()));
            miHelp.DropDownItems.Add(Item("&Ueber ...", Keys.None, (s, e) => ShowAbout()));

            _menu.Items.Add(miFile); _menu.Items.Add(miRun); _menu.Items.Add(miHelp);
            MainMenuStrip = _menu;
            Controls.Add(_menu);
        }

        private static ToolTip MakeTip(string title)
        {
            return new ToolTip { AutoPopDelay = 30000, InitialDelay = 350, ReshowDelay = 80,
                ShowAlways = true, ToolTipTitle = title, IsBalloon = true };
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
            _tabs.TabPages.Add(BuildMetadataTab());
            _tabs.TabPages.Add(BuildWsFedTab());
            _tabs.TabPages.Add(BuildWsTrustTab());
            _tabs.TabPages.Add(BuildSamlTab());
            _tabs.TabPages.Add(BuildOidcTab());
            _tabs.TabPages.Add(BuildReportTab());
            Controls.Add(_tabs);
            _tabs.BringToFront();
        }

        // ---- gemeinsamer Verbindungs-Tab ----
        private TabPage BuildConfigTab()
        {
            var tab = new TabPage("Verbindung") { BackColor = Color.White };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White,
                Padding = new Padding(12, 12, 8, 6), WrapContents = false };
            buttons.Controls.Add(MakeButton("Metadata && Zertifikate laden  (F5)", 250, (s, e) => RunMetadata()));
            var bQuickAll = MakeButton("Schnelltest (alle)  (F6)", 200, (s, e) => RunAll(TestDepth.Quick));
            var bDeepAll = MakeButton("Tiefer Test (alle)  (F7)", 200, (s, e) => RunAll(TestDepth.Deep));
            buttons.Controls.Add(bQuickAll);
            buttons.Controls.Add(bDeepAll);
            _tipBtn.SetToolTip(bQuickAll, QuickTip);
            _tipBtn.SetToolTip(bDeepAll, DeepTip);

            var t = NewFieldTable();
            txtAdfsHost = Row(t, "ADFS-Host / Basis-URL", "z.B. adfs.firma.tld",
                "DNS-Name des ADFS-Dienstes.\n\nADFS-Verwaltung > 'Verbunddiensteigenschaften bearbeiten' > Verbunddienstname,\n" +
                "oder die Login-URL https://<host>/adfs/ls.\nIn eurer SAML-Config = ProviderURL / ProviderDestination (ohne /adfs/ls).\n" +
                "PowerShell: (Get-AdfsProperties).HostName");
            txtRedirectUri = Row(t, "Lokale Callback-URL", "fuer interaktive Tests (SAML-ACS + OAuth redirect_uri)",
                "Auf diese URL laeuft ein lokaler Listener, der die Antwort im interaktiven Test abfaengt.\n\n" +
                "Muss in ADFS erlaubt sein: als SAML Assertion Consumer Service bzw. als OAuth redirect_uri.\n" +
                "Hoher Loopback-Port, z.B. http://localhost:8765/adfs-tester/");
            txtUsername = Row(t, "Testbenutzer", "DOMAIN\\user oder UPN - fuer WS-Trust und OAuth ROPC",
                "Konto fuer die nicht-interaktiven Flows (WS-Trust, OAuth ROPC).\nFormat DOMAIN\\benutzer oder benutzer@firma.tld.\n" +
                "Fuer reine Erreichbarkeits-/Zertifikatstests nicht noetig.");
            txtPassword = Row(t, "Passwort", "(DPAPI-verschluesselt gespeichert)",
                "Passwort des Testbenutzers. Nur lokal DPAPI-verschluesselt gespeichert.", true);
            txtTimeout = Row(t, "Timeout (Sek.)", "Standard 30",
                "Maximale Wartezeit pro Netzwerk-/HTTP-Aufruf. Standard 30.");
            txtCertWarn = Row(t, "Cert-Warnung (Tage)", "Warnung wenn Zertifikat < X Tage gueltig",
                "Zertifikate, die in weniger als dieser Zeit ablaufen, werden gelb markiert. Standard 30.");

            chkShowSecrets = new CheckBox { Text = "Secrets anzeigen", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            chkShowSecrets.CheckedChanged += (s, e) =>
            {
                char pc = chkShowSecrets.Checked ? '\0' : '●';
                txtPassword.PasswordChar = pc; if (txtClientSecret != null) txtClientSecret.PasswordChar = pc;
            };
            chkVerifyCert = new CheckBox { Text = "Server-Zertifikat strikt pruefen (aus = nur diagnostisch)",
                AutoSize = true, Checked = true, Margin = new Padding(3, 4, 3, 6), Cursor = Cursors.Help };
            _tip.SetToolTip(chkVerifyCert,
                "An: HTTP-Aufrufe brechen bei ungueltigem TLS-Zertifikat ab (normaler Modus).\n\n" +
                "Aus: Zertifikatfehler werden bei HTTP-Aufrufen ignoriert (Self-Signed/Testumgebung).\n" +
                "Die Zertifikatspruefung im Tab 'Metadata & Zertifikate' laeuft unabhaengig weiter.");
            AddCheckRow(t, chkShowSecrets);
            AddCheckRow(t, chkVerifyCert);

            tab.Controls.Add(t);
            tab.Controls.Add(buttons);
            return tab;
        }

        private TabPage BuildMetadataTab()
        {
            var tab = new TabPage("Metadata & Zertifikate") { BackColor = Color.White };
            var view = new ResultView(); _views["Metadata"] = view;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White,
                Padding = new Padding(10, 9, 0, 0), WrapContents = false };
            top.Controls.Add(MakeButton("Metadata && Zertifikate laden", 230, (s, e) => RunMetadata()));
            tab.Controls.Add(view);
            tab.Controls.Add(top);
            return tab;
        }

        private TabPage BuildWsFedTab()
        {
            return ProtocolTab("WS-Federation", "WS-Federation", t =>
            {
                txtWsFedRealm = Row(t, "Realm (wtrealm)", "RP-Identifier / Trust Identifier",
                    "Der 'wtrealm' der Anwendung.\n\nADFS > 'Vertrauensstellungen der vertrauenden Seite' >\n" +
                    "<App> > Eigenschaften > Bezeichner.\nPowerShell: Get-AdfsRelyingPartyTrust | Select Name,Identifier");
                txtWsFedReply = Row(t, "wreply (optional)", "WS-Fed Reply-URL fuer nicht-interaktiv",
                    "Optionale Antwort-URL. ADFS > RP-Trust > Endpunkte > WS-Federation Passive Endpoint.\n" +
                    "Im interaktiven Test nutzt das Tool die lokale Callback-URL.");
            });
        }

        private TabPage BuildWsTrustTab()
        {
            return ProtocolTab("WS-Trust", "WS-Trust", t =>
            {
                txtWsTrustAppliesTo = Row(t, "RP-Identifier (AppliesTo)", "Ziel der Token-Anforderung",
                    "Der RP-Identifier, fuer den das Token ausgestellt wird (AppliesTo).\n\n" +
                    "Meist identisch mit dem Bezeichner der Relying Party in ADFS.");
                InfoRow(t, "Nutzt Testbenutzer + Passwort vom Tab 'Verbindung' (usernamemixed-Endpoint, ohne Browser).");
            });
        }

        private TabPage BuildSamlTab()
        {
            return ProtocolTab("SAML 2.0", "SAML 2.0", t =>
            {
                txtSamlRp = Row(t, "RP-Identifier", "Issuer / Audience (RelyingPartyIdentifier)",
                    "SAML Issuer/Audience der Anwendung.\n\nIn eurer CNSamlProvider-Config = RelyingPartyIdentifier\n" +
                    "(z.B. https://sso-casenet.../).\nADFS > RP-Trust > Bezeichner.");
                chkSamlSign = RowCheck(t, "AuthnRequest signieren (SignRequest)",
                    "Entspricht SignRequest=\"True\" in eurer Config: ADFS verlangt einen signierten\n" +
                    "AuthnRequest. Das Tool signiert dann per HTTP-Redirect-Binding (RSA-SHA256) mit dem\n" +
                    "unten gewaehlten Zertifikat (privater Schluessel noetig).");
                cmbSamlSignLoc = RowCombo(t, "Signatur-Zert Store", "Speicherort", new[] { "LocalMachine", "CurrentUser" },
                    "Store-Ort des Signatur-Zertifikats.\nIn eurer Config: SignatureCertStoreLocation (LocalMachine).");
                txtSamlSignStore = Row(t, "Signatur-Zert Store-Name", "z.B. My oder Root",
                    "Store-Name. In eurer Config: SignatureCertStoreName (Root). Ueblich fuer eigene Schluessel: My.");
                txtSamlSignThumb = Row(t, "Signatur-Zert Thumbprint", "Fingerabdruck des Signatur-Zertifikats",
                    "In eurer Config: SignatureCertThumbprint.\nZertifikat muss den privaten Schluessel enthalten (zum Signieren).");
                cmbSamlDecLoc = RowCombo(t, "Encryption-Zert Store", "Speicherort", new[] { "LocalMachine", "CurrentUser" },
                    "Store-Ort des Encryption-Zertifikats (zum Entschluesseln verschluesselter Assertions).\n" +
                    "In eurer Config: EncryptionCertStoreLocation.");
                txtSamlDecStore = Row(t, "Encryption-Zert Store-Name", "z.B. My oder Root",
                    "Store-Name. In eurer Config: EncryptionCertStoreName (Root).");
                txtSamlDecThumb = Row(t, "Encryption-Zert Thumbprint", "fuer verschluesselte Assertions (optional)",
                    "In eurer Config: EncryptionCertThumbprint.\nNur noetig, wenn ADFS die Assertion verschluesselt (EncryptedAssertion).\n" +
                    "Zertifikat muss den privaten Schluessel enthalten.");
                txtSamlClaim = Row(t, "Erwarteter Claim (optional)", "z.B. upn oder emailaddress",
                    "Optional: dieser Claim muss im Token vorkommen.\nIn eurer Config: Claim (z.B. .../claims/upn).\n" +
                    "Teil-/Endstueck des Namens genuegt (z.B. 'upn').");
                InfoRow(t, "Interaktiver Modus: der System-Browser oeffnet die Anmeldung; die SAMLResponse wird ueber die lokale Callback-URL ausgewertet.");
            });
        }

        private TabPage BuildOidcTab()
        {
            return ProtocolTab("OIDC / OAuth", "OIDC / OAuth", t =>
            {
                txtClientId = Row(t, "ClientId", "registrierte Client-ID",
                    "Client-Bezeichner der OAuth/OIDC-Anwendung.\nADFS > 'Anwendungsgruppen' > <Gruppe> > Anwendung > Clientbezeichner.");
                txtClientSecret = Row(t, "ClientSecret", "(DPAPI-verschluesselt gespeichert)",
                    "Geheimnis vertraulicher Clients (Server Application). Native/Public Clients: leer lassen.", true);
                txtScope = Row(t, "Scope", "z.B. openid",
                    "OAuth/OIDC-Scopes. 'openid' fuer ein id_token.");
                txtResource = Row(t, "Resource (optional)", "ADFS 'resource' = RP-Identifier der Ziel-API",
                    "ADFS-spezifischer resource-Parameter (aeltere Flows). = Bezeichner der Web-API in der Anwendungsgruppe.");
                InfoRow(t, "Client-Credentials: ClientId + ClientSecret. ROPC: zusaetzlich Testbenutzer vom Tab 'Verbindung'. Authorization-Code: interaktiv (Callback-URL).");
            });
        }

        // Baut einen Protokoll-Tab: Felder (oben) + Run-Button + Ergebnis-Ansicht.
        private TabPage ProtocolTab(string title, string key, Action<TableLayoutPanel> addFields)
        {
            var tab = new TabPage(title) { BackColor = Color.White };
            var view = new ResultView(); _views[key] = view;
            var t = NewFieldTable();
            addFields(t);
            var bQuick = MakeButton("Schnelltest", 150, (s, e) => RunSingle(key, TestDepth.Quick));
            var bDeep = MakeButton("Tiefer Test", 150, (s, e) => RunSingle(key, TestDepth.Deep));
            bQuick.Margin = new Padding(3, 8, 8, 8);
            bDeep.Margin = new Padding(0, 8, 3, 8);
            _tipBtn.SetToolTip(bQuick, QuickTip);
            _tipBtn.SetToolTip(bDeep, DeepTip);
            var btnRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            btnRow.Controls.Add(bQuick); btnRow.Controls.Add(bDeep);
            t.Controls.Add(new Label()); t.Controls.Add(btnRow); t.Controls.Add(new Label());
            tab.Controls.Add(view);
            tab.Controls.Add(t);
            return tab;
        }

        private TableLayoutPanel NewFieldTable()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true,
                BackColor = Color.White, Padding = new Padding(12, 10, 12, 8),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            return t;
        }

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
            if (!string.IsNullOrEmpty(tooltip))
            { _tip.SetToolTip(lbl, tooltip); _tip.SetToolTip(box, tooltip); _tip.SetToolTip(hintLbl, tooltip); }
            return box;
        }

        private ComboBox RowCombo(TableLayoutPanel t, string label, string hint, string[] items, string tooltip)
        {
            var lbl = new Label { Text = "ⓘ " + label, AutoSize = false, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = LABEL_FORE, Margin = new Padding(3, 5, 3, 5),
                Cursor = Cursors.Help };
            t.Controls.Add(lbl);
            var cmb = new ComboBox { Dock = DockStyle.Left, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3, 5, 3, 5) };
            cmb.Items.AddRange(items);
            cmb.SelectedIndex = 0;
            t.Controls.Add(cmb);
            var hintLbl = new Label { Text = hint, AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray, Margin = new Padding(8, 5, 3, 5) };
            t.Controls.Add(hintLbl);
            if (!string.IsNullOrEmpty(tooltip)) { _tip.SetToolTip(lbl, tooltip); _tip.SetToolTip(cmb, tooltip); }
            return cmb;
        }

        private CheckBox RowCheck(TableLayoutPanel t, string text, string tooltip)
        {
            t.Controls.Add(new Label());
            var cb = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3), Cursor = Cursors.Help };
            t.Controls.Add(cb);
            t.Controls.Add(new Label());
            if (!string.IsNullOrEmpty(tooltip)) _tip.SetToolTip(cb, tooltip);
            return cb;
        }

        private void InfoRow(TableLayoutPanel t, string text)
        {
            t.Controls.Add(new Label());
            var l = new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(0, 103, 192),
                Margin = new Padding(3, 8, 3, 3), MaximumSize = new Size(700, 0) };
            t.Controls.Add(l);
            t.Controls.Add(new Label());
        }

        private static void AddCheckRow(TableLayoutPanel t, CheckBox cb)
        {
            t.Controls.Add(new Label());
            t.Controls.Add(cb);
            t.Controls.Add(new Label());
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
                RedirectUri = txtRedirectUri.Text.Trim(),
                Username = txtUsername.Text.Trim(),
                Password = txtPassword.Text,
                TimeoutSeconds = ParseInt(txtTimeout.Text, 30),
                CertWarnDays = ParseInt(txtCertWarn.Text, 30),
                VerifyServerCert = chkVerifyCert.Checked,

                WsFedRealm = txtWsFedRealm.Text.Trim(),
                WsFedReply = txtWsFedReply.Text.Trim(),

                WsTrustAppliesTo = txtWsTrustAppliesTo.Text.Trim(),

                SamlRpIdentifier = txtSamlRp.Text.Trim(),
                SamlSignRequest = chkSamlSign.Checked,
                SamlSignStoreLocation = cmbSamlSignLoc.SelectedItem != null ? cmbSamlSignLoc.SelectedItem.ToString() : "LocalMachine",
                SamlSignStoreName = txtSamlSignStore.Text.Trim(),
                SamlSignThumbprint = txtSamlSignThumb.Text.Trim(),
                SamlDecryptStoreLocation = cmbSamlDecLoc.SelectedItem != null ? cmbSamlDecLoc.SelectedItem.ToString() : "LocalMachine",
                SamlDecryptStoreName = txtSamlDecStore.Text.Trim(),
                SamlDecryptThumbprint = txtSamlDecThumb.Text.Trim(),
                SamlExpectedClaim = txtSamlClaim.Text.Trim(),

                ClientId = txtClientId.Text.Trim(),
                ClientSecret = txtClientSecret.Text,
                Scope = txtScope.Text.Trim(),
                OAuthResource = txtResource.Text.Trim()
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

        private void RunAll(TestDepth depth)
        {
            string mode = depth == TestDepth.Deep ? "Tiefer Test" : "Schnelltest";
            RunInBackground(mode + ": alle Protokolle ...", cfg =>
            {
                _allRuns.Clear();
                var meta = new TestRun("Metadata & Zertifikate");
                EndpointCertChecks(meta, cfg);
                _md = MetadataClient.Load(meta, cfg);

                var wsfed = WsFedTester.Run(cfg, _md, depth);
                var wstrust = WsTrustTester.Run(cfg, _md, depth);
                var saml = SamlTester.Run(cfg, _md, depth);
                var oidc = OidcTester.Run(cfg, _md, depth);

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

        private void RunSingle(string key, TestDepth depth)
        {
            string mode = depth == TestDepth.Deep ? "Tiefer Test" : "Schnelltest";
            RunInBackground(mode + ": " + key + " ...", cfg =>
            {
                if (_md == null)
                {
                    var meta = new TestRun("Metadata & Zertifikate");
                    EndpointCertChecks(meta, cfg);
                    _md = MetadataClient.Load(meta, cfg);
                    BeginInvoke(new Action(() => { _views["Metadata"].Populate(meta); StoreRun(meta); }));
                }
                TestRun run;
                switch (key)
                {
                    case "WS-Federation": run = WsFedTester.Run(cfg, _md, depth); break;
                    case "WS-Trust": run = WsTrustTester.Run(cfg, _md, depth); break;
                    case "SAML 2.0": run = SamlTester.Run(cfg, _md, depth); break;
                    case "OIDC / OAuth": run = OidcTester.Run(cfg, _md, depth); break;
                    default: return;
                }
                BeginInvoke(new Action(() => { _views[key].Populate(run); StoreRun(run); }));
            });
        }

        private void EndpointCertChecks(TestRun run, AppConfig cfg)
        {
            try
            {
                var uri = new Uri(cfg.BaseUrl);
                int port = uri.Port > 0 ? uri.Port : 443;
                CertificateInspector.InspectEndpoint(run, uri.Host, port, cfg.CertWarnDays, cfg.TimeoutSeconds);
            }
            catch (Exception ex) { run.Add(ErrorLogger.ToCheckResult("ADFS-Host", "URL '" + cfg.AdfsHost + "'", ex)); }
        }

        private void RunInBackground(string statusText, Action<AppConfig> work)
        {
            if (_busy) { MessageBox.Show("Es laeuft bereits ein Test.", "Bitte warten", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            AppConfig cfg = BuildConfig();
            if (string.IsNullOrEmpty(cfg.AdfsHost))
            { MessageBox.Show("Bitte ADFS-Host angeben (Tab 'Verbindung').", "Eingabe fehlt", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

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
                txtAdfsHost.Text = c.AdfsHost; txtRedirectUri.Text = c.RedirectUri;
                txtUsername.Text = c.Username; txtPassword.Text = c.Password;
                txtTimeout.Text = c.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
                txtCertWarn.Text = c.CertWarnDays.ToString(CultureInfo.InvariantCulture);
                chkVerifyCert.Checked = c.VerifyServerCert;

                txtWsFedRealm.Text = c.WsFedRealm; txtWsFedReply.Text = c.WsFedReply;
                txtWsTrustAppliesTo.Text = c.WsTrustAppliesTo;

                txtSamlRp.Text = c.SamlRpIdentifier;
                chkSamlSign.Checked = c.SamlSignRequest;
                SelectCombo(cmbSamlSignLoc, c.SamlSignStoreLocation);
                txtSamlSignStore.Text = c.SamlSignStoreName; txtSamlSignThumb.Text = c.SamlSignThumbprint;
                SelectCombo(cmbSamlDecLoc, c.SamlDecryptStoreLocation);
                txtSamlDecStore.Text = c.SamlDecryptStoreName; txtSamlDecThumb.Text = c.SamlDecryptThumbprint;
                txtSamlClaim.Text = c.SamlExpectedClaim;

                txtClientId.Text = c.ClientId; txtClientSecret.Text = c.ClientSecret;
                txtScope.Text = c.Scope; txtResource.Text = c.OAuthResource;
            }
            catch (Exception ex) { SetStatus("Config-Laden fehlgeschlagen: " + ex.Message, Color.FromArgb(196, 43, 28)); }
        }

        private static void SelectCombo(ComboBox cmb, string value)
        {
            if (cmb == null) return;
            int idx = cmb.Items.IndexOf(value ?? "");
            cmb.SelectedIndex = idx >= 0 ? idx : 0;
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
                "Tab 'Verbindung': ADFS-Host, lokale Callback-URL und (fuer nicht-interaktive Tests)\r\n" +
                "Testbenutzer/Passwort. Dann 'Metadata & Zertifikate laden' - das prueft TLS, Kette und\r\n" +
                "Token-Zertifikate.\r\n\r\n" +
                "Jeder Protokoll-Tab enthaelt genau seine eigenen Felder und zwei Buttons:\r\n" +
                "  Schnelltest : ohne Browser/Zugangsdaten - prueft nur Erreichbarkeit,\r\n" +
                "                Endpunkte und Konfiguration. Ideal fuer einen ersten Check.\r\n" +
                "  Tiefer Test : End-to-End - echte Anmeldung (Browser bzw. Zugangsdaten),\r\n" +
                "                Token holen, Signatur/Claims pruefen (SAML: Signieren/Entschluesseln).\r\n\r\n" +
                "Felder je Tab:\r\n" +
                "  WS-Federation : Realm (wtrealm)\r\n" +
                "  WS-Trust      : RP-Identifier (nutzt Testbenutzer vom Tab 'Verbindung')\r\n" +
                "  SAML 2.0      : RP-Identifier, optional AuthnRequest signieren + Encryption-Zert\r\n" +
                "  OIDC / OAuth  : ClientId/Secret, Scope, Resource\r\n\r\n" +
                "Der Tiefe Test oeffnet fuer WS-Fed/SAML/OAuth-Code den Browser; die Callback-URL\r\n" +
                "(Tab 'Verbindung') muss dafuer in ADFS registriert sein.\r\n\r\n" +
                "Jedes Feld hat einen Tooltip mit dem Fundort in ADFS bzw. in eurer Config.",
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
