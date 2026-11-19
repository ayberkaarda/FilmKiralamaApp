using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Services;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The admin's reports: the most rented films and the late fees of a period, the rentals overdue today,
    /// each saved as CSV with the period it is shown with.
    /// </summary>
    public sealed class RaporlarForm : AppDialog
    {
        internal const string SaveFailed = "Dosya kaydedilemedi; dosyanın başka bir programda açık olmadığından ve klasöre yazma izniniz olduğundan emin olun.";

        private const string CsvFilter = "CSV dosyası (*.csv)|*.csv";

        private readonly IReportService _reports;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _export = new();
        private readonly RequestSequence _loads = new();
        private (ReportKind Kind, ReportRange Range)? _shown;

        private readonly ThemedComboBox cmbRapor = FormLayout.Picker(nameof(cmbRapor), 230);
        private readonly DatePicker dtpBaslangic = new() { Name = nameof(dtpBaslangic), Format = DateTimePickerFormat.Short, Width = 140 };
        private readonly DatePicker dtpBitis = new() { Name = nameof(dtpBitis), Format = DateTimePickerFormat.Short, Width = 140 };
        private readonly ThemedButton btnGoster = FormLayout.Button(nameof(btnGoster), "Göster", ButtonKind.Primary, Glyphs.Chart);
        private readonly ThemedGrid dgvRapor = new() { Name = nameof(dgvRapor) };
        private readonly Label lblToplam = FormLayout.Text(nameof(lblToplam));
        private readonly ThemedButton btnCsv = FormLayout.Button(nameof(btnCsv), "CSV olarak kaydet", ButtonKind.Secondary, Glyphs.Download);

        public RaporlarForm(IReportService reports, UserSession session, TimeProvider timeProvider)
            : base("Raporlar", "Dönemin en çok kiralananları, bugün gecikenler ve gecikme ücretleri; CSV olarak kaydedin.", Glyphs.Chart, new Size(1040, 660), new Size(840, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(reports);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(timeProvider);

            _reports = reports;
            _session = session;
            SavePrompt = new SaveFileDialogPrompt(this);

            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            cmbRapor.Items.AddRange([new ReportChoice(ReportKind.MostRented, "En çok kiralanan filmler"), new ReportChoice(ReportKind.Overdue, "Geciken kiralamalar (bugün)"), new ReportChoice(ReportKind.LateFees, "Gecikme ücretleri")]);
            cmbRapor.SelectedIndex = 0;
            dtpBaslangic.Value = today.AddDays(-30).ToDateTime(TimeOnly.MinValue);
            dtpBitis.Value = today.ToDateTime(TimeOnly.MinValue);
            BuildLayout();
            _export.HoldClosingOf(this);

            btnCsv.Enabled = false;
            cmbRapor.SelectedIndexChanged += (_, _) => UpdateDates();
            btnGoster.Click += async (_, _) => await this.RunAsync(LoadAsync);
            btnCsv.Click += async (_, _) => await ExportCsvAsync();
            UpdateDates();
        }

        /// <summary>The save dialog; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ISaveFilePrompt SavePrompt { get; set; }

        /// <summary>The report shown now and the period it was asked for.</summary>
        internal (ReportKind Kind, ReportRange Range)? ShownReport => _shown;

        /// <summary>The report and period chosen in the fields.</summary>
        internal (ReportKind Kind, ReportRange Range) Chosen =>
            (((ReportChoice)cmbRapor.SelectedItem!).Kind, new ReportRange(DateOnly.FromDateTime(dtpBaslangic.Value), DateOnly.FromDateTime(dtpBitis.Value)));

        /// <summary>Loads the chosen report; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync()
        {
            var request = _loads.Next();
            var (kind, range) = Chosen;
            dgvRapor.IsLoading = true;
            try
            {
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

                dgvRapor.DataSource = rows;
                dgvRapor.ClearSelection();
                lblToplam.Text = total;
                _shown = (kind, range);
                btnCsv.Enabled = true;
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    dgvRapor.IsLoading = false;
                }
            }
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
                btnCsv,
                btnGoster);

        private void UpdateDates()
        {
            // The overdue report is about today; it has no period.
            var period = cmbRapor.SelectedItem is ReportChoice { Kind: not ReportKind.Overdue };
            dtpBaslangic.Enabled = period;
            dtpBitis.Enabled = period;
        }

        private void BuildLayout()
        {
            Name = nameof(RaporlarForm);
            var filters = FormLayout.FieldRow(
                "reportRow",
                (FormLayout.Field("&Rapor", cmbRapor), 40),
                (FormLayout.Field("&Başlangıç", FormLayout.Date(dtpBaslangic)), 22),
                (FormLayout.Field("B&itiş", FormLayout.Date(dtpBitis)), 22),
                (FormLayout.FieldAction(btnGoster), 0));
            var filterCard = FormLayout.Card("filterCard", null, filters, fill: false);
            filterCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, Spacing.XS);
            filterCard.Margin = new Padding(0, 0, 0, Spacing.L);

            dgvRapor.EmptyText = "Raporu ve dönemi seçip Göster'e basın.";
            dgvRapor.FitColumns = new HashSet<string> { "Sıra", "Kiralama", "No", "Gecikme", "Ücret", "Alış", "İade", "Planlanan" };
            lblToplam.Font = Theme.Fonts.BodyStrong;
            lblToplam.Margin = new Padding(0, Spacing.M, 0, 0);
            var reportCard = FormLayout.Card("reportCard", null, FormLayout.Page("reportPage", (dgvRapor, true), (lblToplam, false)));
            reportCard.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("reportsPage", (filterCard, false), (reportCard, true)));

            AcceptButton = btnGoster;
            SetFooterNote("CSV dosyası gösterilen raporu, gösterildiği dönemle kaydeder.");
            SetFooterButtons(btnCsv, CreateCloseButton("btnKapat"));
            FinishLayout();
        }

        private sealed record ReportChoice(ReportKind Kind, string Text)
        {
            public override string ToString() => Text;
        }
    }
}
