using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PCBuildCompanion
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private const string PcPartPickerUrl = "https://pcpartpicker.com/";
        private const string BuildCoresUrl = "https://buildcores.com/";

        // A standard desktop Chrome UA string. Some sites behave differently (or block)
        // when they detect a WebView2/embedded browser, so we present as regular Chrome.
        private const string DesktopUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        private enum Site { PcPartPicker, BuildCores }
        private Site _currentSite = Site.PcPartPicker;

        private string _pcPartPickerLastUrl = PcPartPickerUrl;
        private string _buildCoresLastUrl = BuildCoresUrl;

        // UI controls
        private readonly Panel _topBar = new();
        private readonly Button _backBtn = new();
        private readonly Button _forwardBtn = new();
        private readonly Button _reloadBtn = new();
        private readonly Button _pcPartPickerBtn = new();
        private readonly Button _buildCoresBtn = new();
        private readonly Label _urlLabel = new();
        private readonly Label _statusLabel = new();
        private readonly WebView2 _webView = new();

        // Error overlay controls
        private readonly Panel _errorOverlay = new();
        private readonly Label _errorTitleLabel = new();
        private readonly Label _errorDetailLabel = new();
        private readonly Button _openExternalBtn = new();

        // Parts panel controls (paste-and-copy helper, no automation of either site)
        private readonly Button _partsPanelToggleBtn = new();
        private readonly Panel _partsPanel = new();
        private bool _partsPanelOpen = false;
        private readonly Label _partsPanelTitle = new();
        private readonly Label _partsPanelInstructions = new();
        private readonly TextBox _pastePartsBox = new();
        private readonly Button _parsePartsBtn = new();
        private readonly Button _copyAllRemainingBtn = new();
        private readonly Button _backToPasteBtn = new();
        private readonly Panel _partsChecklistPanel = new(); // scrollable list of parsed parts
        private readonly List<PartRow> _partRows = new();

        private sealed class PartRow
        {
            public string Category = "";
            public string Name = "";
            public bool Copied;
            public Panel? RowControl;
            public Label? NameLabel;
            public Button? CopyButton;
        }

        // AI assistant panel controls (chat backed by the user's own Google AI / Gemini API key)
        private readonly Button _aiPanelToggleBtn = new();
        private readonly Panel _aiPanel = new();
        private bool _aiPanelOpen = false;
        private readonly Label _aiPanelTitle = new();
        private readonly Button _aiSettingsToggleBtn = new();
        private readonly Panel _aiSettingsPanel = new();
        private readonly Label _aiApiKeyLabel = new();
        private readonly TextBox _aiApiKeyBox = new();
        private readonly Label _aiModelLabel = new();
        private readonly TextBox _aiModelBox = new();
        private readonly Button _aiSaveKeyBtn = new();
        private readonly Label _aiApiKeyHint = new();
        private readonly TextBox _aiChatHistoryBox = new();
        private readonly TextBox _aiChatInputBox = new();
        private readonly Button _aiSendBtn = new();
        private readonly Label _aiStatusLabel = new();
        private readonly Button _aiClearChatBtn = new();

        private static readonly HttpClient _httpClient = new();
        private string _aiApiKey = "";
        private string _aiModel = "gemini-flash-latest";
        private bool _aiRequestInFlight = false;
        private readonly List<(string Role, string Text)> _aiChatHistory = new();

        private sealed class AiSettings
        {
            public string ApiKey { get; set; } = "";
            public string Model { get; set; } = "gemini-flash-latest";
        }

        // Colors
        private static readonly Color BgDark = Color.FromArgb(30, 31, 36);
        private static readonly Color BarBg = Color.FromArgb(35, 36, 41);
        private static readonly Color BtnBg = Color.FromArgb(42, 43, 50);
        private static readonly Color BtnActiveBg = Color.FromArgb(61, 126, 255);
        private static readonly Color TextMuted = Color.FromArgb(125, 127, 138);
        private static readonly Color TextNormal = Color.FromArgb(199, 201, 209);

        public MainForm()
        {
            Text = "PC Build Companion";
            Width = 1400;
            Height = 900;
            MinimumSize = new Size(800, 500);
            BackColor = BgDark;
            TrySetWindowIcon();

            BuildTopBar();
            BuildErrorOverlay();
            BuildWebView();
            BuildPartsPanel();
            BuildAiPanel();

            // Order matters: WebView fills remaining space, top bar docks top,
            // error overlay sits above the webview, side panels dock right of the webview.
            Controls.Add(_webView);
            Controls.Add(_errorOverlay);
            Controls.Add(_partsPanel);
            Controls.Add(_aiPanel);
            Controls.Add(_topBar);
            _topBar.BringToFront();
            _partsPanel.BringToFront();
            _aiPanel.BringToFront();

            ShowPasteView();
            LoadAiSettings();

            Load += MainForm_Load;
        }

        private void TrySetWindowIcon()
        {
            try
            {
                using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("PCBuildCompanion.app.ico");
                if (stream != null)
                    Icon = new Icon(stream);
            }
            catch
            {
                // Fall back to the default WinForms icon if the embedded resource is missing
                // or can't be loaded for some reason — not worth crashing the app over.
            }
        }

        private void BuildTopBar()
        {
            _topBar.Dock = DockStyle.Top;
            _topBar.Height = 52;
            _topBar.BackColor = BarBg;
            _topBar.Padding = new Padding(10, 8, 10, 8);

            StyleIconButton(_backBtn, "\u2190", "Back");
            _backBtn.Location = new Point(10, 9);
            _backBtn.Click += (s, e) => { if (_webView.CoreWebView2 != null && _webView.CanGoBack) _webView.CoreWebView2.GoBack(); };

            StyleIconButton(_forwardBtn, "\u2192", "Forward");
            _forwardBtn.Location = new Point(48, 9);
            _forwardBtn.Click += (s, e) => { if (_webView.CoreWebView2 != null && _webView.CanGoForward) _webView.CoreWebView2.GoForward(); };

            StyleIconButton(_reloadBtn, "\u21bb", "Reload");
            _reloadBtn.Location = new Point(86, 9);
            _reloadBtn.Click += (s, e) => _webView.CoreWebView2?.Reload();

            StyleToggleButton(_pcPartPickerBtn, "PCPartPicker");
            _pcPartPickerBtn.Location = new Point(134, 8);
            _pcPartPickerBtn.Width = 120;
            _pcPartPickerBtn.Click += (s, e) => NavigateToSite(Site.PcPartPicker);

            StyleToggleButton(_buildCoresBtn, "BuildCores");
            _buildCoresBtn.Location = new Point(258, 8);
            _buildCoresBtn.Width = 110;
            _buildCoresBtn.Click += (s, e) => NavigateToSite(Site.BuildCores);

            StyleToggleButton(_partsPanelToggleBtn, "\U0001F4CB Parts List");
            _partsPanelToggleBtn.Location = new Point(378, 8);
            _partsPanelToggleBtn.Width = 130;
            _partsPanelToggleBtn.Click += (s, e) => TogglePartsPanel();

            StyleToggleButton(_aiPanelToggleBtn, "\U0001F916 AI Assistant");
            _aiPanelToggleBtn.Location = new Point(518, 8);
            _aiPanelToggleBtn.Width = 150;
            _aiPanelToggleBtn.Click += (s, e) => ToggleAiPanel();

            _urlLabel.AutoSize = false;
            _urlLabel.Location = new Point(684, 0);
            _urlLabel.Size = new Size(496, 52);
            _urlLabel.TextAlign = ContentAlignment.MiddleLeft;
            _urlLabel.ForeColor = TextMuted;
            _urlLabel.Font = new Font("Segoe UI", 9F);
            _urlLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _statusLabel.AutoSize = false;
            _statusLabel.Size = new Size(100, 52);
            _statusLabel.TextAlign = ContentAlignment.MiddleRight;
            _statusLabel.ForeColor = TextMuted;
            _statusLabel.Font = new Font("Segoe UI", 9F);
            _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _statusLabel.Location = new Point(_topBar.Width - 110, 0);

            _topBar.Controls.Add(_backBtn);
            _topBar.Controls.Add(_forwardBtn);
            _topBar.Controls.Add(_reloadBtn);
            _topBar.Controls.Add(_pcPartPickerBtn);
            _topBar.Controls.Add(_buildCoresBtn);
            _topBar.Controls.Add(_partsPanelToggleBtn);
            _topBar.Controls.Add(_aiPanelToggleBtn);
            _topBar.Controls.Add(_urlLabel);
            _topBar.Controls.Add(_statusLabel);
        }

        private void StyleIconButton(Button btn, string glyph, string tooltip)
        {
            btn.Text = glyph;
            btn.Size = new Size(34, 34);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 53, 61);
            btn.BackColor = BarBg;
            btn.ForeColor = TextNormal;
            btn.Font = new Font("Segoe UI", 12F);
            btn.Cursor = Cursors.Hand;
            new ToolTip().SetToolTip(btn, tooltip);
        }

        private void StyleToggleButton(Button btn, string text)
        {
            btn.Text = text;
            btn.Height = 36;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = BtnBg;
            btn.ForeColor = TextNormal;
            btn.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
        }

        private void SetActiveToggle(Button active, Button inactive)
        {
            active.BackColor = BtnActiveBg;
            active.ForeColor = Color.White;
            inactive.BackColor = BtnBg;
            inactive.ForeColor = TextNormal;
        }

        private void BuildErrorOverlay()
        {
            _errorOverlay.Dock = DockStyle.Fill;
            _errorOverlay.BackColor = BgDark;
            _errorOverlay.Visible = false;

            _errorTitleLabel.AutoSize = false;
            _errorTitleLabel.Text = "This page couldn't be displayed";
            _errorTitleLabel.ForeColor = Color.White;
            _errorTitleLabel.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            _errorTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
            _errorTitleLabel.Size = new Size(560, 40);

            _errorDetailLabel.AutoSize = false;
            _errorDetailLabel.ForeColor = Color.FromArgb(169, 171, 181);
            _errorDetailLabel.Font = new Font("Segoe UI", 9.5F);
            _errorDetailLabel.TextAlign = ContentAlignment.MiddleCenter;
            _errorDetailLabel.Size = new Size(560, 100);

            StyleToggleButton(_openExternalBtn, "Open in default browser instead");
            _openExternalBtn.BackColor = BtnActiveBg;
            _openExternalBtn.ForeColor = Color.White;
            _openExternalBtn.Width = 280;
            _openExternalBtn.Click += (s, e) =>
            {
                var url = _currentSite == Site.PcPartPicker ? _pcPartPickerLastUrl : _buildCoresLastUrl;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { /* best effort */ }
            };

            _errorOverlay.Controls.Add(_errorTitleLabel);
            _errorOverlay.Controls.Add(_errorDetailLabel);
            _errorOverlay.Controls.Add(_openExternalBtn);
            _errorOverlay.Resize += (s, e) => CenterErrorControls();
            CenterErrorControls();
        }

        private void CenterErrorControls()
        {
            int centerX = _errorOverlay.Width / 2;
            int centerY = _errorOverlay.Height / 2;

            _errorTitleLabel.Location = new Point(centerX - _errorTitleLabel.Width / 2, centerY - 90);
            _errorDetailLabel.Location = new Point(centerX - _errorDetailLabel.Width / 2, centerY - 40);
            _openExternalBtn.Location = new Point(centerX - _openExternalBtn.Width / 2, centerY + 70);
        }

        private void BuildWebView()
        {
            _webView.Dock = DockStyle.Fill;
        }

        private void BuildPartsPanel()
        {
            _partsPanel.Dock = DockStyle.Right;
            _partsPanel.Width = 360;
            _partsPanel.BackColor = BarBg;
            _partsPanel.Visible = false;
            _partsPanel.Padding = new Padding(16);

            _partsPanelTitle.AutoSize = false;
            _partsPanelTitle.Text = "Parts List Helper";
            _partsPanelTitle.ForeColor = Color.White;
            _partsPanelTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            _partsPanelTitle.Dock = DockStyle.Top;
            _partsPanelTitle.Height = 34;

            _partsPanelInstructions.AutoSize = false;
            _partsPanelInstructions.ForeColor = TextMuted;
            _partsPanelInstructions.Font = new Font("Segoe UI", 8.75F);
            _partsPanelInstructions.Dock = DockStyle.Top;
            _partsPanelInstructions.Height = 90;
            _partsPanelInstructions.Text =
                "On PCPartPicker, open your list and click the \"Markup\" button above the " +
                "part table, then copy the text it gives you. Paste it below.";

            _pastePartsBox.Multiline = true;
            _pastePartsBox.ScrollBars = ScrollBars.Vertical;
            _pastePartsBox.Dock = DockStyle.Top;
            _pastePartsBox.Height = 220;
            _pastePartsBox.BackColor = Color.FromArgb(24, 25, 29);
            _pastePartsBox.ForeColor = TextNormal;
            _pastePartsBox.Font = new Font("Consolas", 9F);
            _pastePartsBox.BorderStyle = BorderStyle.FixedSingle;
            _pastePartsBox.PlaceholderText = "Paste PCPartPicker markup text here...";

            StyleToggleButton(_parsePartsBtn, "Parse Parts List");
            _parsePartsBtn.BackColor = BtnActiveBg;
            _parsePartsBtn.ForeColor = Color.White;
            _parsePartsBtn.Dock = DockStyle.Top;
            _parsePartsBtn.Margin = new Padding(0, 10, 0, 0);
            _parsePartsBtn.Click += (s, e) => ParsePastedParts();

            // Checklist view (shown after parsing)
            _partsChecklistPanel.Dock = DockStyle.Fill;
            _partsChecklistPanel.AutoScroll = true;
            _partsChecklistPanel.Visible = false;
            _partsChecklistPanel.BackColor = BarBg;

            StyleToggleButton(_copyAllRemainingBtn, "Copy All Remaining");
            _copyAllRemainingBtn.BackColor = BtnActiveBg;
            _copyAllRemainingBtn.ForeColor = Color.White;
            _copyAllRemainingBtn.Dock = DockStyle.Bottom;
            _copyAllRemainingBtn.Visible = false;
            _copyAllRemainingBtn.Click += (s, e) => CopyAllRemaining();

            StyleToggleButton(_backToPasteBtn, "\u2190 Paste a different list");
            _backToPasteBtn.Dock = DockStyle.Bottom;
            _backToPasteBtn.Margin = new Padding(0, 0, 0, 8);
            _backToPasteBtn.Visible = false;
            _backToPasteBtn.Click += (s, e) => ShowPasteView();

            // Add in reverse dock order (Bottom docks added first stack correctly)
            _partsPanel.Controls.Add(_partsChecklistPanel);
            _partsPanel.Controls.Add(_copyAllRemainingBtn);
            _partsPanel.Controls.Add(_backToPasteBtn);
            _partsPanel.Controls.Add(_parsePartsBtn);
            _partsPanel.Controls.Add(_pastePartsBox);
            _partsPanel.Controls.Add(_partsPanelInstructions);
            _partsPanel.Controls.Add(_partsPanelTitle);
        }

        private void BuildAiPanel()
        {
            _aiPanel.Dock = DockStyle.Right;
            _aiPanel.Width = 380;
            _aiPanel.BackColor = BarBg;
            _aiPanel.Visible = false;
            _aiPanel.Padding = new Padding(16);

            _aiPanelTitle.AutoSize = false;
            _aiPanelTitle.Text = "AI Build Assistant";
            _aiPanelTitle.ForeColor = Color.White;
            _aiPanelTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            _aiPanelTitle.Dock = DockStyle.Fill;
            _aiPanelTitle.TextAlign = ContentAlignment.MiddleLeft;

            StyleIconButton(_aiSettingsToggleBtn, "\u2699", "API key settings");
            _aiSettingsToggleBtn.Dock = DockStyle.Right;
            _aiSettingsToggleBtn.Click += (s, e) => _aiSettingsPanel.Visible = !_aiSettingsPanel.Visible;

            var titleRow = new Panel { Dock = DockStyle.Top, Height = 34 };
            titleRow.Controls.Add(_aiSettingsToggleBtn);
            titleRow.Controls.Add(_aiPanelTitle);

            // Settings sub-panel: API key + model, collapsible via the gear button above.
            _aiSettingsPanel.Dock = DockStyle.Top;
            _aiSettingsPanel.Height = 210;
            _aiSettingsPanel.Margin = new Padding(0, 8, 0, 0);
            _aiSettingsPanel.Visible = true;

            _aiApiKeyLabel.AutoSize = false;
            _aiApiKeyLabel.Text = "Google AI (Gemini) API key";
            _aiApiKeyLabel.ForeColor = TextMuted;
            _aiApiKeyLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _aiApiKeyLabel.Dock = DockStyle.Top;
            _aiApiKeyLabel.Height = 20;

            _aiApiKeyBox.Dock = DockStyle.Top;
            _aiApiKeyBox.BackColor = Color.FromArgb(24, 25, 29);
            _aiApiKeyBox.ForeColor = TextNormal;
            _aiApiKeyBox.Font = new Font("Consolas", 9F);
            _aiApiKeyBox.BorderStyle = BorderStyle.FixedSingle;
            _aiApiKeyBox.UseSystemPasswordChar = true;
            _aiApiKeyBox.PlaceholderText = "Paste your API key here...";

            _aiModelLabel.AutoSize = false;
            _aiModelLabel.Text = "Model";
            _aiModelLabel.ForeColor = TextMuted;
            _aiModelLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _aiModelLabel.Dock = DockStyle.Top;
            _aiModelLabel.Height = 20;
            _aiModelLabel.Margin = new Padding(0, 8, 0, 0);

            _aiModelBox.Dock = DockStyle.Top;
            _aiModelBox.BackColor = Color.FromArgb(24, 25, 29);
            _aiModelBox.ForeColor = TextNormal;
            _aiModelBox.Font = new Font("Consolas", 9F);
            _aiModelBox.BorderStyle = BorderStyle.FixedSingle;
            _aiModelBox.Text = _aiModel;

            StyleToggleButton(_aiSaveKeyBtn, "Save");
            _aiSaveKeyBtn.BackColor = BtnActiveBg;
            _aiSaveKeyBtn.ForeColor = Color.White;
            _aiSaveKeyBtn.Dock = DockStyle.Top;
            _aiSaveKeyBtn.Margin = new Padding(0, 10, 0, 0);
            _aiSaveKeyBtn.Click += (s, e) => SaveAiSettings();

            _aiApiKeyHint.AutoSize = false;
            _aiApiKeyHint.ForeColor = TextMuted;
            _aiApiKeyHint.Font = new Font("Segoe UI", 7.75F);
            _aiApiKeyHint.Dock = DockStyle.Top;
            _aiApiKeyHint.Height = 50;
            _aiApiKeyHint.Margin = new Padding(0, 8, 0, 0);
            _aiApiKeyHint.Text =
                "Get a free key at aistudio.google.com/apikey. It's stored only in a local " +
                "settings file on this PC and sent directly from this app to Google's API.";

            _aiSettingsPanel.Controls.Add(_aiApiKeyHint);
            _aiSettingsPanel.Controls.Add(_aiSaveKeyBtn);
            _aiSettingsPanel.Controls.Add(_aiModelBox);
            _aiSettingsPanel.Controls.Add(_aiModelLabel);
            _aiSettingsPanel.Controls.Add(_aiApiKeyBox);
            _aiSettingsPanel.Controls.Add(_aiApiKeyLabel);

            // Chat input row (bottom)
            _aiChatInputBox.Multiline = true;
            _aiChatInputBox.ScrollBars = ScrollBars.Vertical;
            _aiChatInputBox.Dock = DockStyle.Bottom;
            _aiChatInputBox.Height = 64;
            _aiChatInputBox.BackColor = Color.FromArgb(24, 25, 29);
            _aiChatInputBox.ForeColor = TextNormal;
            _aiChatInputBox.Font = new Font("Segoe UI", 9F);
            _aiChatInputBox.BorderStyle = BorderStyle.FixedSingle;
            _aiChatInputBox.PlaceholderText = "Ask about compatibility, bottlenecks, upgrades...";
            _aiChatInputBox.Margin = new Padding(0, 10, 0, 0);
            _aiChatInputBox.Click += (s, e) => ActiveControl = _aiChatInputBox;
            _aiChatInputBox.GotFocus += (s, e) => ActiveControl = _aiChatInputBox;
            _aiChatInputBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    SendAiMessage();
                }
            };

            StyleToggleButton(_aiSendBtn, "Send");
            _aiSendBtn.BackColor = BtnActiveBg;
            _aiSendBtn.ForeColor = Color.White;
            _aiSendBtn.Dock = DockStyle.Bottom;
            _aiSendBtn.Margin = new Padding(0, 6, 0, 0);
            _aiSendBtn.Click += (s, e) => SendAiMessage();

            _aiStatusLabel.AutoSize = false;
            _aiStatusLabel.Dock = DockStyle.Bottom;
            _aiStatusLabel.Height = 20;
            _aiStatusLabel.ForeColor = TextMuted;
            _aiStatusLabel.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            _aiStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

            StyleToggleButton(_aiClearChatBtn, "Clear conversation");
            _aiClearChatBtn.Dock = DockStyle.Bottom;
            _aiClearChatBtn.Margin = new Padding(0, 0, 0, 8);
            _aiClearChatBtn.Click += (s, e) =>
            {
                _aiChatHistory.Clear();
                _aiChatHistoryBox.Clear();
            };

            // Chat history fills whatever space is left
            _aiChatHistoryBox.Multiline = true;
            _aiChatHistoryBox.ReadOnly = true;
            _aiChatHistoryBox.ScrollBars = ScrollBars.Vertical;
            _aiChatHistoryBox.Dock = DockStyle.Fill;
            _aiChatHistoryBox.BackColor = Color.FromArgb(24, 25, 29);
            _aiChatHistoryBox.ForeColor = TextNormal;
            _aiChatHistoryBox.Font = new Font("Segoe UI", 9F);
            _aiChatHistoryBox.BorderStyle = BorderStyle.FixedSingle;
            _aiChatHistoryBox.Margin = new Padding(0, 10, 0, 0);

            // Add in reverse dock order so the stack ends up in the right visual order.
            _aiPanel.Controls.Add(_aiChatHistoryBox);
            _aiPanel.Controls.Add(_aiChatInputBox);
            _aiPanel.Controls.Add(_aiSendBtn);
            _aiPanel.Controls.Add(_aiStatusLabel);
            _aiPanel.Controls.Add(_aiClearChatBtn);
            _aiPanel.Controls.Add(_aiSettingsPanel);
            _aiPanel.Controls.Add(titleRow);
        }

        private string AiSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCBuildCompanion", "ai-settings.json");

        private void LoadAiSettings()
        {
            try
            {
                if (File.Exists(AiSettingsPath))
                {
                    var json = File.ReadAllText(AiSettingsPath);
                    var settings = JsonSerializer.Deserialize<AiSettings>(json);
                    if (settings != null)
                    {
                        _aiApiKey = settings.ApiKey ?? "";
                        _aiModel = string.IsNullOrWhiteSpace(settings.Model) ? "gemini-flash-latest" : settings.Model;
                    }
                }
            }
            catch
            {
                // Ignore a missing/corrupt settings file; the user can just re-enter their key.
            }

            _aiApiKeyBox.Text = _aiApiKey;
            _aiModelBox.Text = _aiModel;

            // If a key is already saved, collapse the settings panel so chat has more room.
            if (!string.IsNullOrWhiteSpace(_aiApiKey))
                _aiSettingsPanel.Visible = false;
        }

        private void SaveAiSettings()
        {
            _aiApiKey = _aiApiKeyBox.Text.Trim();
            _aiModel = string.IsNullOrWhiteSpace(_aiModelBox.Text) ? "gemini-flash-latest" : _aiModelBox.Text.Trim();

            try
            {
                var dir = Path.GetDirectoryName(AiSettingsPath);
                if (dir != null) Directory.CreateDirectory(dir);

                var settings = new AiSettings { ApiKey = _aiApiKey, Model = _aiModel };
                File.WriteAllText(AiSettingsPath, JsonSerializer.Serialize(settings));

                _aiStatusLabel.Text = "Settings saved.";
                if (!string.IsNullOrWhiteSpace(_aiApiKey))
                    _aiSettingsPanel.Visible = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save settings: " + ex.Message, "Save failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Gives the model context about what this app is and, if available, the user's parsed parts list.
        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("You are a knowledgeable PC hardware build assistant embedded in a Windows desktop ")
              .Append("app called PC Build Companion, used alongside PCPartPicker and BuildCores. Give ")
              .Append("practical, concise advice about part compatibility, bottlenecks, price-to-performance, ")
              .Append("power supply sizing, cooling, and general build questions. If you're unsure about ")
              .Append("current prices or the newest hardware releases, say so rather than guessing. Reply in ")
              .Append("plain text (no markdown tables or headers), since this is shown in a simple text box.")
              .Append("\n\nImportant pricing context: as of 2026 there is a severe, well-documented global DRAM ")
              .Append("and NAND flash shortage (fabs have shifted wafer capacity toward AI/HBM production), ")
              .Append("which has driven RAM and NVMe SSD prices sharply higher across the entire market. When ")
              .Append("discussing RAM or NVMe/SSD prices, do not describe them as \"overpriced,\" a \"bad deal,\" ")
              .Append("or similar — these price levels reflect a genuine industry-wide supply shortage, not ")
              .Append("price gouging by any particular seller or listing. It's fine to note that prices are ")
              .Append("elevated due to the shortage and to suggest buying only the capacity actually needed, ")
              .Append("but don't tell the user a specific RAM or NVMe listing is overpriced on that basis alone.");

            if (_partRows.Count > 0)
            {
                sb.Append("\n\nThe user's current parts list (parsed from PCPartPicker) is:\n");
                foreach (var row in _partRows)
                    sb.Append("- ").Append(row.Category).Append(": ").Append(row.Name).Append('\n');
                sb.Append("Refer to this parts list when it's relevant, for example to check compatibility or suggest upgrades.");
            }

            return sb.ToString();
        }

        private void AppendChatLine(string speaker, string text)
        {
            if (_aiChatHistoryBox.TextLength > 0)
                _aiChatHistoryBox.AppendText(Environment.NewLine + Environment.NewLine);
            _aiChatHistoryBox.AppendText($"{speaker}: {text}");
            _aiChatHistoryBox.SelectionStart = _aiChatHistoryBox.TextLength;
            _aiChatHistoryBox.ScrollToCaret();
        }

        private async void SendAiMessage()
        {
            if (_aiRequestInFlight) return;

            var message = _aiChatInputBox.Text.Trim();
            if (message.Length == 0) return;

            if (string.IsNullOrWhiteSpace(_aiApiKey))
            {
                MessageBox.Show(this,
                    "Add your Google AI (Gemini) API key in the settings above first. Get a free key at " +
                    "aistudio.google.com/apikey.",
                    "API key needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _aiSettingsPanel.Visible = true;
                return;
            }

            _aiChatInputBox.Clear();
            AppendChatLine("You", message);
            _aiChatHistory.Add(("user", message));

            _aiRequestInFlight = true;
            _aiSendBtn.Enabled = false;
            _aiChatInputBox.Enabled = false;
            _aiStatusLabel.Text = "Thinking\u2026";

            try
            {
                var reply = await CallGeminiApiAsync(message);
                _aiChatHistory.Add(("model", reply));
                AppendChatLine("Assistant", reply);
                _aiStatusLabel.Text = "";
            }
            catch (Exception ex)
            {
                AppendChatLine("Error", ex.Message);
                _aiStatusLabel.Text = "";
                // Don't keep a failed turn in the history that gets sent back to the API next time.
                if (_aiChatHistory.Count > 0 && _aiChatHistory[^1].Role == "user")
                    _aiChatHistory.RemoveAt(_aiChatHistory.Count - 1);
            }
            finally
            {
                _aiRequestInFlight = false;
                _aiSendBtn.Enabled = true;
                _aiChatInputBox.Enabled = true;
                _aiChatInputBox.Focus();
            }
        }

        private async Task<string> CallGeminiApiAsync(string userMessage)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_aiModel)}:generateContent";

            var contents = new List<object>();
            foreach (var turn in _aiChatHistory)
                contents.Add(new { role = turn.Role, parts = new[] { new { text = turn.Text } } });
            contents.Add(new { role = "user", parts = new[] { new { text = userMessage } } });

            var payload = new
            {
                contents,
                systemInstruction = new { parts = new[] { new { text = BuildSystemPrompt() } } }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", _aiApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var detail = body;
                try
                {
                    using var errDoc = JsonDocument.Parse(body);
                    if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                        errEl.TryGetProperty("message", out var msgEl))
                    {
                        detail = msgEl.GetString() ?? body;
                    }
                }
                catch
                {
                    // Not JSON, or shaped differently than expected — fall back to the raw body.
                }

                throw new Exception($"API error ({(int)response.StatusCode}): {detail}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                if (root.TryGetProperty("promptFeedback", out var feedback))
                    throw new Exception("No response — the request may have been blocked. Details: " + feedback);
                throw new Exception("No response was returned from the API.");
            }

            var textBuilder = new StringBuilder();
            var firstCandidate = candidates[0];
            if (firstCandidate.TryGetProperty("content", out var contentEl) &&
                contentEl.TryGetProperty("parts", out var partsEl))
            {
                foreach (var part in partsEl.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textEl))
                        textBuilder.Append(textEl.GetString());
                }
            }

            var resultText = textBuilder.ToString();
            if (string.IsNullOrWhiteSpace(resultText))
                throw new Exception("Received an empty response.");

            return resultText;
        }

        private void TogglePartsPanel()
        {
            _partsPanelOpen = !_partsPanelOpen;
            _partsPanel.Visible = _partsPanelOpen;

            if (_partsPanelOpen)
            {
                _partsPanelToggleBtn.BackColor = BtnActiveBg;
                _partsPanelToggleBtn.ForeColor = Color.White;

                if (_aiPanelOpen)
                {
                    _aiPanelOpen = false;
                    _aiPanel.Visible = false;
                    _aiPanelToggleBtn.BackColor = BtnBg;
                    _aiPanelToggleBtn.ForeColor = TextNormal;
                }
            }
            else
            {
                _partsPanelToggleBtn.BackColor = BtnBg;
                _partsPanelToggleBtn.ForeColor = TextNormal;
            }
        }

        private void ToggleAiPanel()
        {
            _aiPanelOpen = !_aiPanelOpen;
            _aiPanel.Visible = _aiPanelOpen;

            if (_aiPanelOpen)
            {
                _aiPanelToggleBtn.BackColor = BtnActiveBg;
                _aiPanelToggleBtn.ForeColor = Color.White;

                if (_partsPanelOpen)
                {
                    _partsPanelOpen = false;
                    _partsPanel.Visible = false;
                    _partsPanelToggleBtn.BackColor = BtnBg;
                    _partsPanelToggleBtn.ForeColor = TextNormal;
                }

                if (string.IsNullOrWhiteSpace(_aiApiKey))
                    _aiSettingsPanel.Visible = true;

                // WebView2 hosts its own native child window and can hang onto OS keyboard
                // focus even after this panel becomes visible. Setting ActiveControl (not just
                // .Focus()) forces WinForms to hand focus off properly so typing actually lands
                // in the textbox instead of being swallowed by the browser control.
                ActiveControl = _aiChatInputBox;
                _aiChatInputBox.Focus();
            }
            else
            {
                _aiPanelToggleBtn.BackColor = BtnBg;
                _aiPanelToggleBtn.ForeColor = TextNormal;
            }
        }

        private void ShowPasteView()
        {
            _pastePartsBox.Visible = true;
            _parsePartsBtn.Visible = true;
            _partsPanelInstructions.Visible = true;
            _partsChecklistPanel.Visible = false;
            _copyAllRemainingBtn.Visible = false;
            _backToPasteBtn.Visible = false;
            _partsChecklistPanel.Controls.Clear();
            _partRows.Clear();
        }

        private void ShowChecklistView()
        {
            _pastePartsBox.Visible = false;
            _parsePartsBtn.Visible = false;
            _partsPanelInstructions.Visible = false;
            _partsChecklistPanel.Visible = true;
            _copyAllRemainingBtn.Visible = true;
            _backToPasteBtn.Visible = true;
        }

        // Parses PCPartPicker's plain-text "Markup" export, which uses one line per part
        // formatted like "Category: Part Name (extra info @ Merchant)". This is the same
        // text a user would copy themselves via PCPartPicker's own share/markup button.
        private void ParsePastedParts()
        {
            var raw = _pastePartsBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                MessageBox.Show(this, "Paste your PCPartPicker parts list text first.", "Nothing to parse",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var parsed = new List<PartRow>();
            var lines = raw.Replace("\r\n", "\n").Split('\n');

            // Known PCPartPicker category labels that start each line in the markup export.
            string[] knownCategories =
            {
                "CPU", "CPU Cooler", "Motherboard", "Memory", "Storage", "Video Card",
                "Case", "Power Supply", "Operating System", "Monitor", "Case Fan",
                "Fan Controller", "Thermal Compound", "Optical Drive", "Sound Card",
                "Wired Network Adapter", "Wireless Network Adapter", "Headphones",
                "Keyboard", "Mouse", "Speakers", "UPS", "Custom"
            };

            foreach (var lineRaw in lines)
            {
                var line = lineRaw.Trim();
                if (line.Length == 0) continue;

                // Match "Category: rest of line"
                var match = Regex.Match(line, @"^([A-Za-z ]+):\s*(.+)$");
                if (!match.Success) continue;

                var category = match.Groups[1].Value.Trim();
                var rest = match.Groups[2].Value.Trim();

                bool isKnown = knownCategories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
                if (!isKnown) continue;

                // Strip trailing price/merchant info like "($199.99 @ Amazon)"
                var name = Regex.Replace(rest, @"\(\$[^)]*\)\s*$", "").Trim();
                if (name.Length == 0) name = rest;

                // Skip PCPartPicker's own summary lines (Total, etc.) that might slip through
                if (category.Equals("Total", StringComparison.OrdinalIgnoreCase)) continue;

                parsed.Add(new PartRow { Category = category, Name = name });
            }

            if (parsed.Count == 0)
            {
                MessageBox.Show(this,
                    "Couldn't find any recognizable parts in that text. Make sure you copied the " +
                    "\"Markup\" text from PCPartPicker's list page (it looks like lines such as " +
                    "\"CPU: Intel Core i7-4790K...\").",
                    "No parts found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _partRows.Clear();
            _partRows.AddRange(parsed);
            RenderChecklist();
            ShowChecklistView();
        }

        private void RenderChecklist()
        {
            _partsChecklistPanel.Controls.Clear();
            _partsChecklistPanel.SuspendLayout();

            // Add in reverse order since we're using Dock = Top for each row
            for (int i = _partRows.Count - 1; i >= 0; i--)
            {
                var row = _partRows[i];

                var rowPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 64,
                    Padding = new Padding(0, 6, 0, 6),
                    BackColor = BarBg
                };

                var categoryLabel = new Label
                {
                    AutoSize = false,
                    Text = row.Category,
                    ForeColor = TextMuted,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    Dock = DockStyle.Top,
                    Height = 16
                };

                var nameLabel = new Label
                {
                    AutoSize = false,
                    Text = row.Name,
                    ForeColor = TextNormal,
                    Font = new Font("Segoe UI", 8.75F),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };

                var copyBtn = new Button
                {
                    Text = "Copy",
                    Width = 60,
                    Dock = DockStyle.Right,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = BtnBg,
                    ForeColor = TextNormal,
                    Cursor = Cursors.Hand
                };
                copyBtn.FlatAppearance.BorderSize = 0;

                var contentRow = new Panel { Dock = DockStyle.Fill };
                contentRow.Controls.Add(nameLabel);
                contentRow.Controls.Add(copyBtn);

                rowPanel.Controls.Add(contentRow);
                rowPanel.Controls.Add(categoryLabel);

                var capturedRow = row;
                copyBtn.Click += (s, e) =>
                {
                    TryCopyToClipboard(capturedRow.Name);
                    capturedRow.Copied = true;
                    nameLabel.ForeColor = TextMuted;
                    nameLabel.Font = new Font(nameLabel.Font, FontStyle.Strikeout);
                    copyBtn.Text = "Copied";
                    copyBtn.BackColor = Color.FromArgb(50, 90, 60);
                };

                row.RowControl = rowPanel;
                row.NameLabel = nameLabel;
                row.CopyButton = copyBtn;

                _partsChecklistPanel.Controls.Add(rowPanel);
            }

            _partsChecklistPanel.ResumeLayout();
        }

        private void CopyAllRemaining()
        {
            var remaining = _partRows.Where(r => !r.Copied).ToList();
            if (remaining.Count == 0)
            {
                MessageBox.Show(this, "All parts are already marked as copied.", "Nothing left",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sb = new StringBuilder();
            foreach (var r in remaining)
                sb.AppendLine(r.Name);

            TryCopyToClipboard(sb.ToString().TrimEnd());

            foreach (var r in remaining)
            {
                r.Copied = true;
                if (r.NameLabel != null)
                {
                    r.NameLabel.ForeColor = TextMuted;
                    r.NameLabel.Font = new Font(r.NameLabel.Font, FontStyle.Strikeout);
                }
                if (r.CopyButton != null)
                {
                    r.CopyButton.Text = "Copied";
                    r.CopyButton.BackColor = Color.FromArgb(50, 90, 60);
                }
            }
        }

        private void TryCopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // Clipboard access can occasionally fail if another app has a lock on it;
                // fail silently rather than crash the app over a copy action.
            }
        }

        private async void MainForm_Load(object? sender, EventArgs e)
        {
            SetActiveToggle(_pcPartPickerBtn, _buildCoresBtn);

            try
            {
                await _webView.EnsureCoreWebView2Async(null);
            }
            catch (Exception ex)
            {
                ShowError(
                    "WebView2 Runtime not found",
                    "This app requires the Microsoft Edge WebView2 Runtime. It usually comes " +
                    "pre-installed on Windows 10/11, but if it's missing, download it from " +
                    "Microsoft's website (search \"WebView2 Runtime download\").\n\nDetails: " + ex.Message);
                return;
            }

            var core = _webView.CoreWebView2;
            core.Settings.UserAgent = DesktopUserAgent;

            core.NavigationStarting += (s, e2) =>
            {
                _statusLabel.Text = "Loading…";
                _errorOverlay.Visible = false;
            };

            core.NavigationCompleted += (s, e2) =>
            {
                _statusLabel.Text = "";
                if (!e2.IsSuccess)
                {
                    ShowError(
                        "Page failed to load",
                        $"Status: {e2.WebErrorStatus}\n\nThis can happen if the site is blocking " +
                        "automated/embedded browsers, or if there's no internet connection.");
                }
            };

            core.SourceChanged += (s, e2) =>
            {
                var uri = core.Source;
                _urlLabel.Text = uri;
                if (_currentSite == Site.PcPartPicker) _pcPartPickerLastUrl = uri;
                else _buildCoresLastUrl = uri;
                _backBtn.Enabled = core.CanGoBack;
                _forwardBtn.Enabled = core.CanGoForward;
            };

            core.NewWindowRequested += (s, e2) =>
            {
                e2.Handled = true;
                core.Navigate(e2.Uri);
            };

            NavigateToSite(Site.PcPartPicker);
        }

        private void NavigateToSite(Site site)
        {
            if (_webView.CoreWebView2 == null) return;

            _currentSite = site;

            if (site == Site.PcPartPicker)
                SetActiveToggle(_pcPartPickerBtn, _buildCoresBtn);
            else
                SetActiveToggle(_buildCoresBtn, _pcPartPickerBtn);

            var target = site == Site.PcPartPicker ? _pcPartPickerLastUrl : _buildCoresLastUrl;
            _webView.CoreWebView2.Navigate(target);
        }

        private void ShowError(string title, string detail)
        {
            _errorTitleLabel.Text = title;
            _errorDetailLabel.Text = detail;
            _errorOverlay.Visible = true;
            _errorOverlay.BringToFront();
            CenterErrorControls();
        }
    }
}
