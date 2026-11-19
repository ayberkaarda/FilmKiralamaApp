using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Services;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The admin's reports: the most rented films and the late fees of a period, the rentals overdue today,
    /// each saved as CSV with the period it is shown with.
    /// </summary>
    public sealed class RaporlarForm : Form
    {
        internal const string SaveFailed = "Dosya kaydedilemedi; dosyanın başka bir programda açık olmadığından ve klasöre yazma izniniz olduğundan emin olun.";

        private const string CsvFilter = "CSV dosyası (*.csv)|*.csv";

        private readonly IReportService _reports;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _export = new();
        private readonly RequestSequence _loads = new();
        private readonly ComboBox _kind;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly Button _show;
        private readonly Button _csv;
        private readonly DataGridView _grid;
        private readonly Label _total;
        private (ReportKind Kind, ReportRange Range)? _shown;

        public RaporlarForm(IReportService reports, UserSession session, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(reports);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(timeProvider);

            _reports = reports;
            _session = session;
            Prompts = new MessageBoxPrompts(this);
            SavePrompt = new SaveFileDialogPrompt(this);

            Text = "Raporlar";
            Icon = AppAssets.Icon;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(1000, 620);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            Label Caption(string text, int x) => new() { Text = text, Location = new Point(x, 12), AutoSize = true };
            _kind = new ComboBox { Name = "cmbRapor", Location = new Point(16, 34), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            _kind.Items.AddRange([new ReportChoice(ReportKind.MostRented, "En çok kiralanan filmler"), new ReportChoice(ReportKind.Overdue, "Geciken kiralamalar (bugün)"), new ReportChoice(ReportKind.LateFees, "Gecikme ücretleri")]);
            _kind.SelectedIndex = 0;
            _from = new DateTimePicker { Name = "dtpBaslangic", Location = new Point(262, 34), Width = 150, Format = DateTimePickerFormat.Short, Value = today.AddDays(-30).ToDateTime(TimeOnly.MinValue) };
            _to = new DateTimePicker { Name = "dtpBitis", Location = new Point(428, 34), Width = 150, Format = DateTimePickerFormat.Short, Value = today.ToDateTime(TimeOnly.MinValue) };
            _show = new Button { Name = "btnGoster", Text = "Göster", Location = new Point(594, 31), Size = new Size(120, 34) };
            _csv = new Button { Name = "btnCsv", Text = "CSV Olarak Kaydet", Location = new Point(830, 31), Size = new Size(154, 34), Enabled = false };
            _grid = new DataGridView
            {
                Name = "dgvRapor",
                Location = new Point(16, 80),
                Size = new Size(968, 490),
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            _grid.UseReadOnlyList();
            _total = new Label { Name = "lblToplam", Location = new Point(16, 582), Size = new Size(968, 26), Font = new Font("Segoe UI", 10, FontStyle.Bold) };

            _kind.SelectedIndexChanged += (_, _) => UpdateDates();
            _show.Click += async (_, _) => await this.RunAsync(LoadAsync);
            _csv.Click += async (_, _) => await ExportCsvAsync();
            Controls.AddRange([Caption("Rapor", 16), _kind, Caption("Başlangıç", 262), _from, Caption("Bitiş", 428), _to, _show, _csv, _grid, _total]);
            UpdateDates();
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The save dialog; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ISaveFilePrompt SavePrompt { get; set; }

        /// <summary>The report shown now and the period it was asked for.</summary>
        internal (ReportKind Kind, ReportRange Range)? ShownReport => _shown;

        /// <summary>The report and period chosen in the fields.</summary>
        internal (ReportKind Kind, ReportRange Range) Chosen =>
            (((ReportChoice)_kind.SelectedItem!).Kind, new ReportRange(DateOnly.FromDateTime(_from.Value), DateOnly.FromDateTime(_to.Value)));

        /// <summary>Loads the chosen report; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync()
        {
            var request = _loads.Next();
            var (kind, range) = Chosen;
            object rows;
            string total;
            switch (kind)
            {
                case ReportKind.MostRented:
                    var counts = await _reports.GetMostRentedAsync(_session, range);
                    rows = counts.Select((r, i) => new { Sıra = i + 1, Film = r.Title, Tür = r.Genre, Kiralama = r.Rentals }).ToList();
                    total = string.Create(CultureInfo.InvariantCulture, $"{counts.Count} film, toplam {counts.Sum(c => c.Rentals)} kiralama");
                    break;
                case ReportKind.Overdue:
                    var overdue = await _reports.GetOverdueAsync(_session);
                    rows = overdue.Select(r => new
                    {
                        No = r.Rental.RentalId,
                        Film = r.Rental.FilmTitle,
                        Kullanıcı = r.Rental.UserName,
                        Alış = DisplayFormat.Date(r.Rental.RentedOn),
                        İade = DisplayFormat.Date(r.Rental.DueOn),
                        Gecikme = r.OverdueDays,
                        Ücret = DisplayFormat.Money(r.AccruedFee),
                    }).ToList();
                    total = string.Create(CultureInfo.InvariantCulture, $"{overdue.Count} geciken kiralama, birikmiş gecikme ücreti {DisplayFormat.Money(overdue.Sum(r => r.AccruedFee))}");
                    break;
                default:
                    var fees = await _reports.GetLateFeesAsync(_session, range);
                    rows = fees.Rows.Select(r => new
                    {
                        No = r.RentalId,
                        Film = r.FilmTitle,
                        Kullanıcı = r.UserName,
                        Planlanan = DisplayFormat.Date(r.DueOn),
                        İade = DisplayFormat.Date(r.ReturnedOn),
                        Ücret = DisplayFormat.Money(r.LateFee),
                    }).ToList();
                    total = string.Create(CultureInfo.InvariantCulture, $"{fees.Rows.Count} kiralama, toplam gecikme ücreti {DisplayFormat.Money(fees.Total)}");
                    break;
            }

            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            _grid.DataSource = rows;
            _grid.ClearSelection();
            _total.Text = total;
            _shown = (kind, range);
            _csv.Enabled = true;
        }

        /// <summary>
        /// Saves the report that is shown, for the period it is shown with, as CSV; ignored while a save is
        /// running.
        /// </summary>
        internal Task<bool> ExportCsvAsync() =>
            this.RunExclusiveAsync(
                _export,
                async () =>
                {
                    if (_shown is not { } shown)
                    {
                        Prompts.Warn("Lütfen önce raporu gösterin.");
                        return;
                    }

                    var file = await _reports.ExportCsvAsync(_session, shown.Kind, shown.Range);
                    if (SavePrompt.AskSavePath(file.SuggestedName, CsvFilter) is not { } path)
                    {
                        return;
                    }

                    try
                    {
                        await ExportFileWriter.WriteAsync(path, file.Content);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Prompts.Warn(SaveFailed);
                        return;
                    }

                    // Recorded only once the file is on the disk.
                    _reports.RecordExport(_session);
                    Prompts.Inform("Rapor kaydedildi.");
                },
                _csv,
                _show);

        private void UpdateDates()
        {
            // The overdue report is about today; it has no period.
            var period = _kind.SelectedItem is ReportChoice { Kind: not ReportKind.Overdue };
            _from.Enabled = period;
            _to.Enabled = period;
        }

        private sealed record ReportChoice(ReportKind Kind, string Text)
        {
            public override string ToString() => Text;
        }
    }
}
