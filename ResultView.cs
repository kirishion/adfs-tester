// Wiederverwendbare Ergebnis-Ansicht: farbcodierte Tabelle der CheckResults
// plus Detail-/Rohdaten-Feld darunter.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace AdfsTester
{
    public sealed class ResultView : Panel
    {
        private readonly DataGridView _grid;
        private readonly TextBox _detail;
        private TestRun _run;

        private static readonly Color OK = Color.FromArgb(16, 124, 16);
        private static readonly Color INFO = Color.FromArgb(0, 103, 192);
        private static readonly Color WARN = Color.FromArgb(183, 121, 31);
        private static readonly Color ERR = Color.FromArgb(196, 43, 28);

        public ResultView()
        {
            Dock = DockStyle.Fill;

            _detail = new TextBox
            {
                Dock = DockStyle.Bottom,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Height = 170,
                BackColor = Color.FromArgb(250, 250, 250),
                Font = new Font("Consolas", 9F)
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9F)
            };
            _grid.Columns.Add(Col("sev", "Status", 70, false));
            _grid.Columns.Add(Col("step", "Schritt", 200, false));
            _grid.Columns.Add(Col("detail", "Detail", 320, true));
            _grid.Columns.Add(Col("rec", "Empfehlung", 280, true));
            _grid.SelectionChanged += (s, e) => ShowDetail();

            var splitInfo = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Text = "  Zeile auswaehlen fuer Rohdaten / vollstaendige Empfehlung:",
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.Add(_grid);
            Controls.Add(splitInfo);
            Controls.Add(_detail);
        }

        private static DataGridViewColumn Col(string name, string header, int width, bool fill)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                FillWeight = fill ? 100 : 1,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            };
        }

        public void Populate(TestRun run)
        {
            _run = run;
            _grid.Rows.Clear();
            _detail.Clear();
            if (run == null) return;
            foreach (var c in run.Checks)
            {
                int idx = _grid.Rows.Add(SevText(c.Severity), c.Step, c.Detail, c.Recommendation);
                var row = _grid.Rows[idx];
                var color = SevColor(c.Severity);
                row.Cells[0].Style.ForeColor = color;
                row.Cells[0].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                if (c.Severity == Severity.Error) row.DefaultCellStyle.BackColor = Color.FromArgb(253, 240, 239);
                else if (c.Severity == Severity.Warning) row.DefaultCellStyle.BackColor = Color.FromArgb(253, 248, 238);
            }
            if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
        }

        private void ShowDetail()
        {
            if (_run == null || _grid.SelectedRows.Count == 0) { _detail.Clear(); return; }
            int idx = _grid.SelectedRows[0].Index;
            if (idx < 0 || idx >= _run.Checks.Count) { _detail.Clear(); return; }
            var c = _run.Checks[idx];
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[" + c.Severity + "] " + c.Step);
            sb.AppendLine(c.Detail);
            if (!string.IsNullOrEmpty(c.Recommendation))
            { sb.AppendLine(); sb.AppendLine("Empfehlung:"); sb.AppendLine(c.Recommendation); }
            if (!string.IsNullOrEmpty(c.RawData))
            { sb.AppendLine(); sb.AppendLine("--- Rohdaten ---"); sb.AppendLine(c.RawData); }
            _detail.Text = sb.ToString();
            _detail.SelectionStart = 0; _detail.SelectionLength = 0;
        }

        private static string SevText(Severity s)
        {
            switch (s) { case Severity.Ok: return "OK"; case Severity.Info: return "Info";
                case Severity.Warning: return "Warnung"; default: return "FEHLER"; }
        }

        private static Color SevColor(Severity s)
        {
            switch (s) { case Severity.Ok: return OK; case Severity.Info: return INFO;
                case Severity.Warning: return WARN; default: return ERR; }
        }
    }
}
