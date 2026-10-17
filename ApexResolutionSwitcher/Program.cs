using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json;

namespace ApexResolutionSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class DisplayChoice
{
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public override string ToString() => $"{FriendlyName} ({DeviceName})";
}

internal sealed class ResolutionChoice
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Frequency { get; init; }
    public int BitsPerPixel { get; init; }
    public override string ToString()
    {
        var ratio = Height == 0 ? 0 : (double)Width / Height;
        var quality = Height >= 2160 ? "4K UHD" : Height >= 1440 ? "1440p QHD" : Height >= 1080 ? "1080p FHD" : Height >= 720 ? "720p HD" : "SD";
        var wide = ratio >= 2.2 ? " · Ultrawide" : "";
        return $"{Width} × {Height} @ {Frequency} Hz · {quality}{wide}";
    }
}

internal sealed class AppSettings
{
    public string MonitorDeviceName { get; set; } = "";
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
    public int TargetFrequency { get; set; }
    public string GameApi { get; set; } = "DirectX 12";
    public string SearchFilter { get; set; } = "";
    public string ResolutionFilter { get; set; } = "All resolutions";
    public string AspectFilter { get; set; } = "All aspect ratios";
    public string RefreshFilter { get; set; } = "All refresh rates";

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ApexResolutionSwitcher", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

internal sealed class MainForm : Form
{
    private readonly ComboBox monitorCombo = new();
    private readonly ComboBox resolutionCombo = new();
    private readonly ComboBox apiCombo = new();
    private readonly TextBox searchBox = new();
    private readonly ComboBox resolutionFilter = new();
    private readonly ComboBox aspectFilter = new();
    private readonly ComboBox refreshFilter = new();
    private readonly AppSettings settings = AppSettings.Load();
    private bool loadingUi = true;
    private bool loadingResolutionList;
    private readonly Button applyButton = new();
    private readonly Button restoreButton = new();
    private readonly Button refreshButton = new();
    private readonly Label statusLabel = new();
    private readonly Label nativeLabel = new();
    private readonly TextBox logBox = new();
    private readonly Dictionary<string, Native.DEVMODE> originalModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer monitorTimer = new() { Interval = 500 };
    private DateTime? gameSeenSince;
    private DateTime? gameMissingSince;
    private bool monitoringGame;
    private bool operationInProgress;
    private string activeDevice = "";
    private string GameProcessName => apiCombo.SelectedIndex == 1 ? "r5apex" : "r5apex_dx12";
    private string GameApiName => apiCombo.SelectedIndex == 1 ? "DirectX 11" : "DirectX 12";

    public MainForm()
    {
        Text = "Apex Resolution Switcher";
        MinimumSize = new Size(820, 730);
        Size = new Size(960, 790);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 25, 35);
        ForeColor = Color.FromArgb(230, 237, 245);
        Font = new Font("Segoe UI", 10F);

        BuildUi();
        LoadDisplays();
        loadingUi = false;
        ApplySavedFilters();
        monitorTimer.Tick += MonitorTimer_Tick;
        FormClosing += (_, e) =>
        {
            if (monitoringGame || operationInProgress)
            {
                var answer = MessageBox.Show(
                    "Apex resolution monitoring is active. Close the app anyway? The desktop may remain at the selected resolution. Choose No to keep the app open, or restore the native resolution first.",
                    "Monitoring is active", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }
            monitorTimer.Stop();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(22),
            BackColor = BackColor,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var heading = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "APEX RESOLUTION SWITCHER",
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(103, 183, 255),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        var subtitle = new Label
        {
            Text = "Use a higher render resolution for Apex while restoring your desktop afterward.",
            ForeColor = Color.FromArgb(169, 184, 200),
            AutoSize = true,
            Location = new Point(2, 38)
        };
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        root.Controls.Add(heading, 0, 0);

        var monitorPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        monitorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        monitorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        monitorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        monitorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitorPanel.Controls.Add(MakeLabel("Monitor"), 0, 0);
        monitorCombo.Dock = DockStyle.Fill;
        StyleCombo(monitorCombo);
        monitorCombo.SelectedIndexChanged += (_, _) => { LoadResolutions(); SaveCurrentSettings(saveResolution: false); };
        monitorPanel.Controls.Add(monitorCombo, 0, 1);
        refreshButton.Text = "Refresh displays";
        StyleButton(refreshButton, secondary: true);
        refreshButton.Dock = DockStyle.Fill;
        refreshButton.Click += (_, _) => LoadDisplays();
        monitorPanel.Controls.Add(refreshButton, 1, 1);
        root.Controls.Add(monitorPanel, 0, 1);

        var resolutionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        resolutionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        resolutionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        resolutionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resolutionPanel.Controls.Add(MakeLabel("Apex / temporary desktop resolution"), 0, 0);
        resolutionCombo.Dock = DockStyle.Fill;
        StyleCombo(resolutionCombo);
        resolutionCombo.SelectedIndexChanged += (_, _) => { if (!loadingUi && !loadingResolutionList) SaveCurrentSettings(); };
        resolutionPanel.Controls.Add(resolutionCombo, 0, 1);
        nativeLabel.Text = "Native resolution: detecting…";
        nativeLabel.ForeColor = Color.FromArgb(169, 184, 200);
        nativeLabel.Dock = DockStyle.Fill;
        nativeLabel.TextAlign = ContentAlignment.MiddleLeft;
        resolutionPanel.Controls.Add(nativeLabel, 0, 2);
        root.Controls.Add(resolutionPanel, 0, 2);

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = new Padding(0) };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        filters.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filters.Controls.Add(MakeLabel("Search resolutions"), 0, 0);
        filters.Controls.Add(MakeLabel("Resolution group"), 1, 0);
        filters.Controls.Add(MakeLabel("Aspect ratio"), 2, 0);
        filters.Controls.Add(MakeLabel("Refresh rate"), 3, 0);
        searchBox.Dock = DockStyle.Fill;
        searchBox.BackColor = Color.FromArgb(38, 52, 67);
        searchBox.ForeColor = Color.White;
        searchBox.BorderStyle = BorderStyle.FixedSingle;
        searchBox.Font = new Font("Segoe UI", 10F);
        searchBox.TextChanged += (_, _) => { if (!loadingUi) { LoadResolutions(); SaveCurrentSettings(saveResolution: false); } };
        filters.Controls.Add(searchBox, 0, 1);
        ConfigureFilter(resolutionFilter, new[] { "All resolutions", "Above native", "1080p and higher", "1440p and higher", "4K and higher", "Ultrawide" });
        ConfigureFilter(aspectFilter, new[] { "All aspect ratios", "16:9", "16:10", "21:9", "32:9", "Other" });
        ConfigureFilter(refreshFilter, new[] { "All refresh rates", "60 Hz", "75 Hz", "120 Hz+", "144 Hz+", "165 Hz+", "240 Hz+" });
        resolutionFilter.SelectedIndexChanged += (_, _) => { if (!loadingUi) { LoadResolutions(); SaveCurrentSettings(saveResolution: false); } };
        aspectFilter.SelectedIndexChanged += (_, _) => { if (!loadingUi) { LoadResolutions(); SaveCurrentSettings(saveResolution: false); } };
        refreshFilter.SelectedIndexChanged += (_, _) => { if (!loadingUi) { LoadResolutions(); SaveCurrentSettings(saveResolution: false); } };
        filters.Controls.Add(resolutionFilter, 1, 1);
        filters.Controls.Add(aspectFilter, 2, 1);
        filters.Controls.Add(refreshFilter, 3, 1);
        root.Controls.Add(filters, 0, 3);

        var apiPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        apiPanel.Controls.Add(MakeLabel("Apex rendering API"), 0, 0);
        ConfigureFilter(apiCombo, new[] { "DirectX 12 (r5apex_dx12)", "DirectX 11 (r5apex)" });
        apiCombo.SelectedIndexChanged += (_, _) => SaveCurrentSettings();
        apiPanel.Controls.Add(apiCombo, 1, 0);
        root.Controls.Add(apiPanel, 0, 4);

        var buttonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        applyButton.Text = "Apply resolution & monitor Apex";
        StyleButton(applyButton, secondary: false);
        applyButton.Dock = DockStyle.Fill;
        applyButton.Click += async (_, _) => await ApplyAndMonitorAsync();
        buttonPanel.Controls.Add(applyButton, 0, 0);
        restoreButton.Text = "Switch back to native";
        StyleButton(restoreButton, secondary: true);
        restoreButton.Dock = DockStyle.Fill;
        restoreButton.Click += (_, _) => RestoreNative("Manual restore requested.");
        buttonPanel.Controls.Add(restoreButton, 1, 0);
        root.Controls.Add(buttonPanel, 0, 5);

        statusLabel.Text = "Ready. Select a supported resolution to begin.";
        statusLabel.ForeColor = Color.FromArgb(103, 183, 255);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(statusLabel, 0, 6);

        logBox.Dock = DockStyle.Fill;
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BackColor = Color.FromArgb(12, 17, 24);
        logBox.ForeColor = Color.FromArgb(205, 218, 231);
        logBox.BorderStyle = BorderStyle.FixedSingle;
        logBox.Font = new Font("Consolas", 9.5F);
        root.Controls.Add(logBox, 0, 7);
        Log("Welcome. Your monitor, target resolution, filters, and DX version are saved locally.");
        Log("For AMD, enable Virtual Super Resolution; for NVIDIA, enable DSR if needed.");
        Log("Keep this app open while Apex is running so it can restore your desktop.");
    }

    private void ConfigureFilter(ComboBox combo, IEnumerable<string> items)
    {
        combo.Dock = DockStyle.Fill;
        StyleCombo(combo);
        combo.Items.AddRange(items.Cast<object>().ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void ApplySavedFilters()
    {
        loadingUi = true;
        searchBox.Text = settings.SearchFilter;
        SelectComboText(resolutionFilter, settings.ResolutionFilter);
        SelectComboText(aspectFilter, settings.AspectFilter);
        SelectComboText(refreshFilter, settings.RefreshFilter);
        SelectComboText(apiCombo, settings.GameApi == "DirectX 11" ? "DirectX 11 (r5apex)" : "DirectX 12 (r5apex_dx12)");
        loadingUi = false;
        LoadResolutions();
        SaveCurrentSettings(saveResolution: false);
    }

    private static void SelectComboText(ComboBox combo, string text)
    {
        var index = -1;
        for (var i = 0; i < combo.Items.Count; i++)
        {
            var item = combo.Items[i]?.ToString() ?? "";
            if (item == text) { index = i; break; }
        }
        if (index >= 0) combo.SelectedIndex = index;
        else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void SaveCurrentSettings(bool saveResolution = true)
    {
        if (loadingUi) return;
        if (monitorCombo.SelectedItem is DisplayChoice display) settings.MonitorDeviceName = display.DeviceName;
        if (saveResolution && resolutionCombo.SelectedItem is ResolutionChoice resolution)
        {
            settings.TargetWidth = resolution.Width;
            settings.TargetHeight = resolution.Height;
            settings.TargetFrequency = resolution.Frequency;
        }
        settings.GameApi = GameApiName;
        settings.SearchFilter = searchBox.Text;
        settings.ResolutionFilter = resolutionFilter.SelectedItem?.ToString() ?? "All resolutions";
        settings.AspectFilter = aspectFilter.SelectedItem?.ToString() ?? "All aspect ratios";
        settings.RefreshFilter = refreshFilter.SelectedItem?.ToString() ?? "All refresh rates";
        settings.Save();
    }

    private Label MakeLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(220, 230, 240),
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
    };

    private void StyleCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Color.FromArgb(38, 52, 67);
        combo.ForeColor = Color.White;
        combo.Font = new Font("Segoe UI", 10F);
    }

    private void StyleButton(Button button, bool secondary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = secondary ? Color.FromArgb(70, 91, 111) : Color.FromArgb(35, 116, 190);
        button.BackColor = secondary ? Color.FromArgb(37, 49, 63) : Color.FromArgb(23, 107, 186);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 4, 8, 4);
    }

    private void LoadDisplays()
    {
        var previous = monitorCombo.SelectedItem as DisplayChoice;
        monitorCombo.Items.Clear();
        var displays = Native.GetAttachedDisplays();
        foreach (var display in displays)
        {
            monitorCombo.Items.Add(display);
            if (!originalModes.ContainsKey(display.DeviceName) && Native.TryGetCurrentMode(display.DeviceName, out var mode))
                originalModes[display.DeviceName] = mode;
        }

        if (displays.Count == 0)
        {
            statusLabel.Text = "No active displays were found.";
            return;
        }

        var preferredIndex = 0;
        if (previous != null)
        {
            var found = displays.FindIndex(d => d.DeviceName.Equals(previous.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) preferredIndex = found;
        }
        else
        {
            var saved = displays.FindIndex(d => d.DeviceName.Equals(settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (saved >= 0) preferredIndex = saved;
            else
            {
                var primary = displays.FindIndex(d => d.DeviceName.Equals(Native.GetPrimaryDisplayName(), StringComparison.OrdinalIgnoreCase));
                if (primary >= 0) preferredIndex = primary;
            }
        }

        monitorCombo.SelectedIndex = preferredIndex;
        LoadResolutions();
        Log($"Found {displays.Count} active display(s).");
    }

    private void LoadResolutions()
    {
        if (monitorCombo.SelectedItem is not DisplayChoice display) return;
        var current = Native.TryGetCurrentMode(display.DeviceName, out var active) ? active : default;
        var native = originalModes.TryGetValue(display.DeviceName, out var savedNative) ? savedNative : current;
        var nativePixels = (long)native.dmPelsWidth * native.dmPelsHeight;

        var modes = Native.GetModes(display.DeviceName)
            .Where(m => m.Width >= 640 && m.Height >= 480 && m.Frequency >= 24)
            .GroupBy(m => (m.Width, m.Height, m.Frequency))
            .Select(g => g.First())
            .OrderBy(m => m.Width * (long)m.Height)
            .ThenBy(m => m.Frequency)
            .ToList();

        IEnumerable<ResolutionChoice> filtered = modes;
        var search = searchBox.Text.Trim();
        if (search.Length > 0)
            filtered = filtered.Where(m => m.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || $"{m.Width}x{m.Height}".Contains(search, StringComparison.OrdinalIgnoreCase));

        var group = resolutionFilter.SelectedItem?.ToString() ?? "All resolutions";
        filtered = group switch
        {
            "Above native" => filtered.Where(m => (long)m.Width * m.Height > nativePixels),
            "1080p and higher" => filtered.Where(m => m.Height >= 1080),
            "1440p and higher" => filtered.Where(m => m.Height >= 1440),
            "4K and higher" => filtered.Where(m => m.Width >= 3840 || m.Height >= 2160),
            "Ultrawide" => filtered.Where(m => (double)m.Width / m.Height >= 2.2),
            _ => filtered
        };

        var aspect = aspectFilter.SelectedItem?.ToString() ?? "All aspect ratios";
        filtered = aspect switch
        {
            "16:9" => filtered.Where(m => AspectNear(m, 16.0 / 9.0, 0.035)),
            "16:10" => filtered.Where(m => AspectNear(m, 16.0 / 10.0, 0.035)),
            "21:9" => filtered.Where(m => AspectNear(m, 21.0 / 9.0, 0.07)),
            "32:9" => filtered.Where(m => AspectNear(m, 32.0 / 9.0, 0.08)),
            "Other" => filtered.Where(m => !AspectNear(m, 16.0 / 9.0, 0.035) && !AspectNear(m, 16.0 / 10.0, 0.035) && !AspectNear(m, 21.0 / 9.0, 0.07) && !AspectNear(m, 32.0 / 9.0, 0.08)),
            _ => filtered
        };

        var refresh = refreshFilter.SelectedItem?.ToString() ?? "All refresh rates";
        filtered = refresh switch
        {
            "60 Hz" => filtered.Where(m => m.Frequency == 60),
            "75 Hz" => filtered.Where(m => m.Frequency == 75),
            "120 Hz+" => filtered.Where(m => m.Frequency >= 120),
            "144 Hz+" => filtered.Where(m => m.Frequency >= 144),
            "165 Hz+" => filtered.Where(m => m.Frequency >= 165),
            "240 Hz+" => filtered.Where(m => m.Frequency >= 240),
            _ => filtered
        };

        var filteredModes = filtered.ToList();
        loadingResolutionList = true;
        resolutionCombo.BeginUpdate();
        resolutionCombo.Items.Clear();
        foreach (var mode in filteredModes) resolutionCombo.Items.Add(mode);

        var match = filteredModes.FindIndex(m => m.Width == settings.TargetWidth && m.Height == settings.TargetHeight && m.Frequency == settings.TargetFrequency);
        if (match < 0) match = filteredModes.FindIndex(m => m.Width == current.dmPelsWidth && m.Height == current.dmPelsHeight && m.Frequency == current.dmDisplayFrequency);
        if (match < 0) match = filteredModes.FindIndex(m => m.Width == current.dmPelsWidth && m.Height == current.dmPelsHeight);
        if (match >= 0) resolutionCombo.SelectedIndex = match;
        else if (filteredModes.Count > 0) resolutionCombo.SelectedIndex = 0;
        resolutionCombo.EndUpdate();
        loadingResolutionList = false;

        nativeLabel.Text = $"Native for this session: {native.dmPelsWidth} × {native.dmPelsHeight} @ {native.dmDisplayFrequency} Hz";
        statusLabel.Text = $"Showing {filteredModes.Count} of {modes.Count} supported modes.";
    }

    private static bool AspectNear(ResolutionChoice mode, double target, double tolerance)
        => Math.Abs((double)mode.Width / mode.Height - target) <= tolerance;

    private async Task ApplyAndMonitorAsync()
    {
        if (operationInProgress || monitoringGame) return;
        if (monitorCombo.SelectedItem is not DisplayChoice display || resolutionCombo.SelectedItem is not ResolutionChoice resolution)
        {
            MessageBox.Show("Select a monitor and resolution first.", "Missing selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (Process.GetProcessesByName(GameProcessName).Length > 0)
        {
            MessageBox.Show($"Apex ({GameApiName}) appears to already be running. Close Apex first, then apply the resolution and launch the game.", "Apex already running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        operationInProgress = true;
        SetControlsEnabled(false);
        activeDevice = display.DeviceName;
        try
        {
            if (!originalModes.ContainsKey(activeDevice) && Native.TryGetCurrentMode(activeDevice, out var original))
                originalModes[activeDevice] = original;

            SaveCurrentSettings();
            Log($"Selected API: {GameApiName} ({GameProcessName}).");
            Log($"Switching {display.FriendlyName} to {resolution.Width} × {resolution.Height} @ {resolution.Frequency} Hz…");
            statusLabel.Text = "Applying selected resolution…";
            var result = Native.SetResolution(activeDevice, resolution.Width, resolution.Height, resolution.Frequency);
            if (result != Native.DISP_CHANGE_SUCCESSFUL)
            {
                Log($"Windows rejected the mode change (code {result}).");
                MessageBox.Show($"Windows could not apply that display mode. Result code: {result}\n\nTry another listed resolution or refresh your GPU's VSR/DSR settings.", "Resolution change failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await Task.Delay(1200);
            monitoringGame = true;
            gameSeenSince = null;
            gameMissingSince = null;
            monitorTimer.Start();
            statusLabel.Text = $"Waiting for Apex {GameApiName} to start…";
            Log($"Resolution applied. Launch Apex ({GameApiName}) through Steam or the EA app now.");
            Log("The app confirms the selected game process stays running for 5 seconds before monitoring its exit.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(ex.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            operationInProgress = false;
            if (!monitoringGame) SetControlsEnabled(true);
        }
    }

    private void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (!monitoringGame) return;
        var processes = Process.GetProcessesByName(GameProcessName);
        var isPresent = processes.Length > 0;
        foreach (var p in processes) p.Dispose();

        if (isPresent)
        {
            gameMissingSince = null;
            gameSeenSince ??= DateTime.Now;
            if (DateTime.Now - gameSeenSince.Value >= TimeSpan.FromSeconds(5))
            {
                statusLabel.Text = "Apex detected. Waiting for the game to close…";
                if (!logBox.Text.Contains("Apex detected; monitoring game exit."))
                    Log("Apex detected; monitoring game exit.");
            }
            else
            {
                statusLabel.Text = "Confirming Apex startup…";
            }
            return;
        }

        // Ignore brief process gaps during startup and between game-process transitions.
        if (gameSeenSince.HasValue && DateTime.Now - gameSeenSince.Value >= TimeSpan.FromSeconds(5))
        {
            gameMissingSince ??= DateTime.Now;
            if (DateTime.Now - gameMissingSince.Value >= TimeSpan.FromSeconds(5))
            {
                monitorTimer.Stop();
                monitoringGame = false;
                Log("Apex closed. Waiting 3 seconds before restoring the desktop…");
                statusLabel.Text = "Apex closed. Restoring native resolution shortly…";
                var timer = new System.Windows.Forms.Timer { Interval = 3000 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    RestoreNative("Apex closed.");
                };
                timer.Start();
            }
        }
        else
        {
            // Apex has not been confirmed running; keep waiting for launch.
            statusLabel.Text = $"Waiting for Apex {GameApiName} to start…";
        }
    }

    private void RestoreNative(string reason)
    {
        if (string.IsNullOrWhiteSpace(activeDevice))
            activeDevice = (monitorCombo.SelectedItem as DisplayChoice)?.DeviceName ?? "";

        monitorTimer.Stop();
        monitoringGame = false;

        if (string.IsNullOrWhiteSpace(activeDevice))
        {
            statusLabel.Text = "No display selected to restore.";
            SetControlsEnabled(true);
            return;
        }

        if (!originalModes.TryGetValue(activeDevice, out var original))
        {
            Log("No saved original resolution is available for this display.");
            statusLabel.Text = "Original display mode unavailable.";
            SetControlsEnabled(true);
            return;
        }

        var result = Native.SetResolution(activeDevice, original.dmPelsWidth, original.dmPelsHeight, original.dmDisplayFrequency);
        if (result == Native.DISP_CHANGE_SUCCESSFUL)
        {
            Log($"Restored native resolution: {original.dmPelsWidth} × {original.dmPelsHeight} @ {original.dmDisplayFrequency} Hz.");
            statusLabel.Text = "Native resolution restored. Ready.";
        }
        else
        {
            Log($"Could not restore saved mode (code {result}). Open Windows Display settings to restore it manually.");
            statusLabel.Text = "Automatic restore failed; check Windows Display settings.";
            MessageBox.Show($"Windows could not restore the saved display mode (code {result}). You can restore it in Windows Display settings.", "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        SetControlsEnabled(true);
    }

    private void SetControlsEnabled(bool enabled)
    {
        monitorCombo.Enabled = enabled;
        resolutionCombo.Enabled = enabled;
        refreshButton.Enabled = enabled;
        apiCombo.Enabled = enabled;
        searchBox.Enabled = enabled;
        resolutionFilter.Enabled = enabled;
        aspectFilter.Enabled = enabled;
        refreshFilter.Enabled = enabled;
        applyButton.Enabled = enabled;
        restoreButton.Enabled = true;
    }

    private void Log(string message)
    {
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}

internal static class Native
{
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int ENUM_REGISTRY_SETTINGS = -2;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DM_DISPLAYFREQUENCY = 0x400000;
    private const int DM_BITSPERPEL = 0x40000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    public static List<DisplayChoice> GetAttachedDisplays()
    {
        var result = new List<DisplayChoice>();
        for (uint i = 0; i < 32; i++)
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref d, 0)) break;
            if ((d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            result.Add(new DisplayChoice { DeviceName = d.DeviceName, FriendlyName = string.IsNullOrWhiteSpace(d.DeviceString) ? d.DeviceName : d.DeviceString });
        }
        return result;
    }

    public static string GetPrimaryDisplayName()
    {
        for (uint i = 0; i < 32; i++)
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref d, 0)) break;
            if ((d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0) return d.DeviceName;
        }
        return "";
    }

    public static bool TryGetCurrentMode(string deviceName, out DEVMODE mode)
    {
        mode = CreateMode();
        return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode);
    }

    public static List<ResolutionChoice> GetModes(string deviceName)
    {
        var modes = new List<ResolutionChoice>();
        for (int i = 0; i < 5000; i++)
        {
            var mode = CreateMode();
            if (!EnumDisplaySettings(deviceName, i, ref mode)) break;
            if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0) continue;
            modes.Add(new ResolutionChoice
            {
                Width = mode.dmPelsWidth,
                Height = mode.dmPelsHeight,
                Frequency = mode.dmDisplayFrequency > 0 ? mode.dmDisplayFrequency : 60,
                BitsPerPixel = mode.dmBitsPerPel
            });
        }
        return modes;
    }

    public static int SetResolution(string deviceName, int width, int height, int frequency)
    {
        if (!TryGetCurrentMode(deviceName, out var mode)) mode = CreateMode();
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmDisplayFrequency = frequency;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
    }

    private static DEVMODE CreateMode() => new()
    {
        dmDeviceName = new string('\0', 32),
        dmFormName = new string('\0', 32),
        dmSize = (short)Marshal.SizeOf<DEVMODE>()
    };
}
