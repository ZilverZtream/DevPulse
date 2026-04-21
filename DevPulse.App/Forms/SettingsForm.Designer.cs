namespace DevPulse.App.Forms;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    // Top-level
    private System.Windows.Forms.TabControl _tabs;
    private System.Windows.Forms.Button _btnSave;

    // Tab pages
    private System.Windows.Forms.TabPage _tabConnection;
    private System.Windows.Forms.TabPage _tabPolling;
    private System.Windows.Forms.TabPage _tabIdentities;
    private System.Windows.Forms.TabPage _tabInboxes;
    private System.Windows.Forms.TabPage _tabBoard;
    private System.Windows.Forms.TabPage _tabNotifications;
    private System.Windows.Forms.TabPage _tabAdvanced;

    // Layouts
    private System.Windows.Forms.TableLayoutPanel _layoutConnection;
    private System.Windows.Forms.TableLayoutPanel _layoutPolling;
    private System.Windows.Forms.TableLayoutPanel _layoutIdentities;
    private System.Windows.Forms.TableLayoutPanel _layoutBoard;
    private System.Windows.Forms.TableLayoutPanel _layoutAdvanced;
    private System.Windows.Forms.SplitContainer _splitInboxes;

    // Connection tab labels + controls
    private System.Windows.Forms.Label _lblOrgUrl;
    private System.Windows.Forms.Label _lblProject;
    private System.Windows.Forms.Label _lblRepoFilter;
    private System.Windows.Forms.Label _lblCurrentUser;
    private System.Windows.Forms.Label _lblPat;
    private System.Windows.Forms.TextBox _orgUrl;
    private System.Windows.Forms.TextBox _project;
    private System.Windows.Forms.TextBox _repoFilter;
    private System.Windows.Forms.TextBox _currentUser;
    private System.Windows.Forms.TextBox _patBox;
    private System.Windows.Forms.Button _btnTest;

    // Polling tab labels + controls
    private System.Windows.Forms.Label _lblPrInterval;
    private System.Windows.Forms.Label _lblWiInterval;
    private System.Windows.Forms.NumericUpDown _prInterval;
    private System.Windows.Forms.NumericUpDown _wiInterval;
    private System.Windows.Forms.CheckBox _refreshOnStartup;

    // Identities tab labels + controls
    private System.Windows.Forms.Label _lblBotPatterns;
    private System.Windows.Forms.Label _lblPoQaGroup;
    private System.Windows.Forms.Label _lblAliases;
    private System.Windows.Forms.TextBox _botPatterns;
    private System.Windows.Forms.TextBox _poQaGroup;
    private System.Windows.Forms.DataGridView _aliasGrid;

    // Inboxes tab controls
    private System.Windows.Forms.ListBox _inboxList;
    private System.Windows.Forms.TextBox _inboxRulesJson;

    // Board tab labels + controls
    private System.Windows.Forms.Label _lblAreaPath;
    private System.Windows.Forms.Label _lblIterationPath;
    private System.Windows.Forms.Label _lblColumns;
    private System.Windows.Forms.TextBox _areaPath;
    private System.Windows.Forms.TextBox _iterationPath;
    private System.Windows.Forms.DataGridView _columnsGrid;

    // Notifications tab
    private System.Windows.Forms.Label _lblNotificationsNote;

    // Advanced tab controls
    private System.Windows.Forms.Button _btnExport;

    // AI tab
    private System.Windows.Forms.TabPage _tabAi;
    private System.Windows.Forms.TextBox _txtAiRoot;
    private System.Windows.Forms.CheckBox _chkClaudeEnabled;
    private System.Windows.Forms.TextBox _txtClaudePath;
    private System.Windows.Forms.Button _btnClaudeDetect;
    private System.Windows.Forms.CheckBox _chkOpenRouterEnabled;
    private System.Windows.Forms.TextBox _txtOpenRouterKey;
    private System.Windows.Forms.TextBox _txtOpenRouterModel;
    private System.Windows.Forms.ListBox _lstAiTemplates;
    private System.Windows.Forms.TextBox _txtTemplateHeaders;
    private System.Windows.Forms.TextBox _txtTemplateBody;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // ── Instantiate all controls ──────────────────────────────────────────
        _tabs = new System.Windows.Forms.TabControl();
        _btnSave = new System.Windows.Forms.Button();

        _tabConnection = new System.Windows.Forms.TabPage();
        _tabPolling = new System.Windows.Forms.TabPage();
        _tabIdentities = new System.Windows.Forms.TabPage();
        _tabInboxes = new System.Windows.Forms.TabPage();
        _tabBoard = new System.Windows.Forms.TabPage();
        _tabNotifications = new System.Windows.Forms.TabPage();
        _tabAdvanced = new System.Windows.Forms.TabPage();

        _layoutConnection = new System.Windows.Forms.TableLayoutPanel();
        _layoutPolling = new System.Windows.Forms.TableLayoutPanel();
        _layoutIdentities = new System.Windows.Forms.TableLayoutPanel();
        _layoutBoard = new System.Windows.Forms.TableLayoutPanel();
        _layoutAdvanced = new System.Windows.Forms.TableLayoutPanel();
        _splitInboxes = new System.Windows.Forms.SplitContainer();

        // Connection controls
        _lblOrgUrl = new System.Windows.Forms.Label();
        _lblProject = new System.Windows.Forms.Label();
        _lblRepoFilter = new System.Windows.Forms.Label();
        _lblCurrentUser = new System.Windows.Forms.Label();
        _lblPat = new System.Windows.Forms.Label();
        _orgUrl = new System.Windows.Forms.TextBox();
        _project = new System.Windows.Forms.TextBox();
        _repoFilter = new System.Windows.Forms.TextBox();
        _currentUser = new System.Windows.Forms.TextBox();
        _patBox = new System.Windows.Forms.TextBox();
        _btnTest = new System.Windows.Forms.Button();

        // Polling controls
        _lblPrInterval = new System.Windows.Forms.Label();
        _lblWiInterval = new System.Windows.Forms.Label();
        _prInterval = new System.Windows.Forms.NumericUpDown();
        _wiInterval = new System.Windows.Forms.NumericUpDown();
        _refreshOnStartup = new System.Windows.Forms.CheckBox();

        // Identities controls
        _lblBotPatterns = new System.Windows.Forms.Label();
        _lblPoQaGroup = new System.Windows.Forms.Label();
        _lblAliases = new System.Windows.Forms.Label();
        _botPatterns = new System.Windows.Forms.TextBox();
        _poQaGroup = new System.Windows.Forms.TextBox();
        _aliasGrid = new System.Windows.Forms.DataGridView();

        // Inboxes controls
        _inboxList = new System.Windows.Forms.ListBox();
        _inboxRulesJson = new System.Windows.Forms.TextBox();

        // Board controls
        _lblAreaPath = new System.Windows.Forms.Label();
        _lblIterationPath = new System.Windows.Forms.Label();
        _lblColumns = new System.Windows.Forms.Label();
        _areaPath = new System.Windows.Forms.TextBox();
        _iterationPath = new System.Windows.Forms.TextBox();
        _columnsGrid = new System.Windows.Forms.DataGridView();

        // Notifications controls
        _lblNotificationsNote = new System.Windows.Forms.Label();

        // Advanced controls
        _btnExport = new System.Windows.Forms.Button();

        SuspendLayout();
        _layoutConnection.SuspendLayout();
        _layoutPolling.SuspendLayout();
        _layoutIdentities.SuspendLayout();
        _layoutBoard.SuspendLayout();
        _layoutAdvanced.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_aliasGrid).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_columnsGrid).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_prInterval).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_wiInterval).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_splitInboxes).BeginInit();
        _splitInboxes.Panel1.SuspendLayout();
        _splitInboxes.Panel2.SuspendLayout();

        // ── Connection tab ────────────────────────────────────────────────────
        _lblOrgUrl.Text = "Organization URL:";
        _lblOrgUrl.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblOrgUrl.AutoSize = true;
        _lblOrgUrl.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblProject.Text = "Project:";
        _lblProject.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblProject.AutoSize = true;
        _lblProject.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblRepoFilter.Text = "Repository filter:";
        _lblRepoFilter.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblRepoFilter.AutoSize = true;
        _lblRepoFilter.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblCurrentUser.Text = "Your email (canonical key):";
        _lblCurrentUser.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblCurrentUser.AutoSize = true;
        _lblCurrentUser.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblPat.Text = "Personal Access Token:";
        _lblPat.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblPat.AutoSize = true;
        _lblPat.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _orgUrl.Dock = System.Windows.Forms.DockStyle.Fill;
        _orgUrl.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _orgUrl.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _project.Dock = System.Windows.Forms.DockStyle.Fill;
        _project.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _project.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _repoFilter.Dock = System.Windows.Forms.DockStyle.Fill;
        _repoFilter.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _repoFilter.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _currentUser.Dock = System.Windows.Forms.DockStyle.Fill;
        _currentUser.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _currentUser.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _patBox.Dock = System.Windows.Forms.DockStyle.Fill;
        _patBox.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _patBox.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _patBox.UseSystemPasswordChar = true;

        _btnTest.Text = "Test connection";
        _btnTest.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnTest.BackColor = System.Drawing.Color.FromArgb(50, 80, 120);
        _btnTest.ForeColor = System.Drawing.Color.White;
        _btnTest.Height = 28;
        _btnTest.Click += new System.EventHandler(BtnTest_Click);

        _layoutConnection.Dock = System.Windows.Forms.DockStyle.Fill;
        _layoutConnection.ColumnCount = 2;
        _layoutConnection.Padding = new System.Windows.Forms.Padding(16);
        _layoutConnection.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _layoutConnection.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 160F));
        _layoutConnection.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutConnection.Controls.Add(_lblOrgUrl, 0, 0);
        _layoutConnection.Controls.Add(_orgUrl, 1, 0);
        _layoutConnection.Controls.Add(_lblProject, 0, 1);
        _layoutConnection.Controls.Add(_project, 1, 1);
        _layoutConnection.Controls.Add(_lblRepoFilter, 0, 2);
        _layoutConnection.Controls.Add(_repoFilter, 1, 2);
        _layoutConnection.Controls.Add(_lblCurrentUser, 0, 3);
        _layoutConnection.Controls.Add(_currentUser, 1, 3);
        _layoutConnection.Controls.Add(_lblPat, 0, 4);
        _layoutConnection.Controls.Add(_patBox, 1, 4);
        _layoutConnection.Controls.Add(new System.Windows.Forms.Label(), 0, 5);
        _layoutConnection.Controls.Add(_btnTest, 1, 5);

        _tabConnection.Text = "Connection";
        _tabConnection.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabConnection.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabConnection.Controls.Add(_layoutConnection);

        // ── Polling tab ───────────────────────────────────────────────────────
        _lblPrInterval.Text = "PR poll interval (minutes):";
        _lblPrInterval.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblPrInterval.AutoSize = true;
        _lblPrInterval.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblWiInterval.Text = "Work item poll interval (minutes):";
        _lblWiInterval.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblWiInterval.AutoSize = true;
        _lblWiInterval.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _prInterval.Minimum = 1;
        _prInterval.Maximum = 60;
        _prInterval.Value = 5;
        _prInterval.Dock = System.Windows.Forms.DockStyle.Fill;
        _prInterval.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _prInterval.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _wiInterval.Minimum = 1;
        _wiInterval.Maximum = 120;
        _wiInterval.Value = 10;
        _wiInterval.Dock = System.Windows.Forms.DockStyle.Fill;
        _wiInterval.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _wiInterval.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _refreshOnStartup.Text = "Refresh on startup";
        _refreshOnStartup.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _layoutPolling.Dock = System.Windows.Forms.DockStyle.Fill;
        _layoutPolling.ColumnCount = 2;
        _layoutPolling.Padding = new System.Windows.Forms.Padding(16);
        _layoutPolling.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _layoutPolling.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 220F));
        _layoutPolling.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutPolling.Controls.Add(_lblPrInterval, 0, 0);
        _layoutPolling.Controls.Add(_prInterval, 1, 0);
        _layoutPolling.Controls.Add(_lblWiInterval, 0, 1);
        _layoutPolling.Controls.Add(_wiInterval, 1, 1);
        _layoutPolling.Controls.Add(new System.Windows.Forms.Label(), 0, 2);
        _layoutPolling.Controls.Add(_refreshOnStartup, 1, 2);

        _tabPolling.Text = "Polling";
        _tabPolling.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabPolling.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabPolling.Controls.Add(_layoutPolling);

        // ── Identities tab ────────────────────────────────────────────────────
        _lblBotPatterns.Text = "Bot identity patterns (comma-sep):";
        _lblBotPatterns.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblBotPatterns.AutoSize = true;
        _lblBotPatterns.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblPoQaGroup.Text = "PO/QA group canonical keys (comma-sep):";
        _lblPoQaGroup.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblPoQaGroup.AutoSize = true;
        _lblPoQaGroup.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblAliases.Text = "Identity aliases:";
        _lblAliases.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblAliases.AutoSize = true;
        _lblAliases.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _botPatterns.Dock = System.Windows.Forms.DockStyle.Fill;
        _botPatterns.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _botPatterns.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _botPatterns.Multiline = false;

        _poQaGroup.Dock = System.Windows.Forms.DockStyle.Fill;
        _poQaGroup.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _poQaGroup.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _poQaGroup.Multiline = false;

        _aliasGrid.BackgroundColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _aliasGrid.GridColor = System.Drawing.Color.FromArgb(60, 60, 80);
        _aliasGrid.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _aliasGrid.DefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _aliasGrid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(38, 38, 56);
        _aliasGrid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _aliasGrid.AllowUserToAddRows = true;
        _aliasGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
        _aliasGrid.Dock = System.Windows.Forms.DockStyle.Fill;
        _aliasGrid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn { Name = "canonical", HeaderText = "Canonical key", Width = 200 });
        _aliasGrid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn { Name = "variants", HeaderText = "Variants (comma-sep)", Width = 300 });

        _layoutIdentities.Dock = System.Windows.Forms.DockStyle.Fill;
        _layoutIdentities.ColumnCount = 2;
        _layoutIdentities.Padding = new System.Windows.Forms.Padding(16);
        _layoutIdentities.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _layoutIdentities.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 220F));
        _layoutIdentities.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutIdentities.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        _layoutIdentities.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        _layoutIdentities.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutIdentities.Controls.Add(_lblBotPatterns, 0, 0);
        _layoutIdentities.Controls.Add(_botPatterns, 1, 0);
        _layoutIdentities.Controls.Add(_lblPoQaGroup, 0, 1);
        _layoutIdentities.Controls.Add(_poQaGroup, 1, 1);
        _layoutIdentities.Controls.Add(_lblAliases, 0, 2);
        _layoutIdentities.Controls.Add(_aliasGrid, 1, 2);

        _tabIdentities.Text = "Identities";
        _tabIdentities.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabIdentities.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabIdentities.Controls.Add(_layoutIdentities);

        // ── Inboxes tab ───────────────────────────────────────────────────────
        _inboxList.Dock = System.Windows.Forms.DockStyle.Fill;
        _inboxList.BackColor = System.Drawing.Color.FromArgb(36, 36, 52);
        _inboxList.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _inboxList.SelectedIndexChanged += new System.EventHandler(InboxList_SelectedIndexChanged);

        _inboxRulesJson.Dock = System.Windows.Forms.DockStyle.Fill;
        _inboxRulesJson.Multiline = true;
        _inboxRulesJson.ScrollBars = System.Windows.Forms.ScrollBars.Both;
        _inboxRulesJson.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _inboxRulesJson.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _inboxRulesJson.Font = new System.Drawing.Font("Consolas", 8.5F);
        _inboxRulesJson.WordWrap = false;

        _splitInboxes.Dock = System.Windows.Forms.DockStyle.Fill;
        _splitInboxes.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _splitInboxes.SplitterDistance = 200;
        _splitInboxes.Panel1.Controls.Add(_inboxList);
        _splitInboxes.Panel2.Controls.Add(_inboxRulesJson);

        _tabInboxes.Text = "Inboxes";
        _tabInboxes.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabInboxes.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabInboxes.Controls.Add(_splitInboxes);

        // ── Board tab ─────────────────────────────────────────────────────────
        _lblAreaPath.Text = "Area path:";
        _lblAreaPath.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblAreaPath.AutoSize = true;
        _lblAreaPath.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblIterationPath.Text = "Iteration path (optional):";
        _lblIterationPath.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblIterationPath.AutoSize = true;
        _lblIterationPath.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _lblColumns.Text = "Board columns:";
        _lblColumns.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblColumns.AutoSize = true;
        _lblColumns.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);

        _areaPath.Dock = System.Windows.Forms.DockStyle.Fill;
        _areaPath.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _areaPath.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _iterationPath.Dock = System.Windows.Forms.DockStyle.Fill;
        _iterationPath.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _iterationPath.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        _columnsGrid.BackgroundColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _columnsGrid.GridColor = System.Drawing.Color.FromArgb(60, 60, 80);
        _columnsGrid.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(42, 42, 62);
        _columnsGrid.DefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _columnsGrid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(38, 38, 56);
        _columnsGrid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _columnsGrid.AllowUserToAddRows = true;
        _columnsGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
        _columnsGrid.Dock = System.Windows.Forms.DockStyle.Fill;
        _columnsGrid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn { Name = "name", HeaderText = "Column", Width = 120 });
        _columnsGrid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn { Name = "states", HeaderText = "ADO States (comma-sep)", Width = 180 });
        _columnsGrid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn { Name = "warn", HeaderText = "Warn days", Width = 80 });
        _columnsGrid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn { Name = "stale", HeaderText = "Stale days", Width = 80 });

        _layoutBoard.Dock = System.Windows.Forms.DockStyle.Fill;
        _layoutBoard.ColumnCount = 2;
        _layoutBoard.Padding = new System.Windows.Forms.Padding(16);
        _layoutBoard.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _layoutBoard.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 180F));
        _layoutBoard.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutBoard.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        _layoutBoard.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        _layoutBoard.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutBoard.Controls.Add(_lblAreaPath, 0, 0);
        _layoutBoard.Controls.Add(_areaPath, 1, 0);
        _layoutBoard.Controls.Add(_lblIterationPath, 0, 1);
        _layoutBoard.Controls.Add(_iterationPath, 1, 1);
        _layoutBoard.Controls.Add(_lblColumns, 0, 2);
        _layoutBoard.Controls.Add(_columnsGrid, 1, 2);

        _tabBoard.Text = "Board";
        _tabBoard.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabBoard.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabBoard.Controls.Add(_layoutBoard);

        // ── Notifications tab ─────────────────────────────────────────────────
        _lblNotificationsNote.Text = "Configure per-inbox notifications on the Inboxes tab.\nNeeds My Attention notifications are always enabled.";
        _lblNotificationsNote.Dock = System.Windows.Forms.DockStyle.Fill;
        _lblNotificationsNote.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _lblNotificationsNote.Padding = new System.Windows.Forms.Padding(16);

        _tabNotifications.Text = "Notifications";
        _tabNotifications.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabNotifications.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabNotifications.Controls.Add(_lblNotificationsNote);

        // ── Advanced tab ──────────────────────────────────────────────────────
        _btnExport.Text = "Export settings JSON\u2026";
        _btnExport.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnExport.BackColor = System.Drawing.Color.FromArgb(50, 80, 120);
        _btnExport.ForeColor = System.Drawing.Color.White;
        _btnExport.Height = 28;
        _btnExport.Click += new System.EventHandler(BtnExport_Click);

        _layoutAdvanced.Dock = System.Windows.Forms.DockStyle.Fill;
        _layoutAdvanced.ColumnCount = 2;
        _layoutAdvanced.Padding = new System.Windows.Forms.Padding(16);
        _layoutAdvanced.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _layoutAdvanced.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 220F));
        _layoutAdvanced.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        _layoutAdvanced.Controls.Add(new System.Windows.Forms.Label(), 0, 0);
        _layoutAdvanced.Controls.Add(_btnExport, 1, 0);

        _tabAdvanced.Text = "Advanced";
        _tabAdvanced.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabAdvanced.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _tabAdvanced.Controls.Add(_layoutAdvanced);

        // ── AI tab ────────────────────────────────────────────────────────────
        _tabAi = new System.Windows.Forms.TabPage();
        _tabAi.Text = "AI";
        _tabAi.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabAi.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

        var aiLayout = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new System.Windows.Forms.Padding(12),
            BackColor = System.Drawing.Color.FromArgb(30, 30, 46)
        };
        aiLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 180));
        aiLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));

        System.Windows.Forms.Label MakeLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Padding = new System.Windows.Forms.Padding(0, 6, 0, 0),
            ForeColor = System.Drawing.Color.FromArgb(180, 180, 200)
        };

        System.Windows.Forms.TextBox MakeTextBox(bool password = false) => new()
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(42, 42, 62),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235),
            UseSystemPasswordChar = password
        };

        _txtAiRoot = MakeTextBox();
        aiLayout.Controls.Add(MakeLabel("Output root:"));
        aiLayout.Controls.Add(_txtAiRoot);

        _chkClaudeEnabled = new System.Windows.Forms.CheckBox
        {
            Text = "Claude Code CLI",
            AutoSize = true,
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235)
        };
        aiLayout.Controls.Add(MakeLabel(""));
        aiLayout.Controls.Add(_chkClaudeEnabled);

        _txtClaudePath = MakeTextBox();
        aiLayout.Controls.Add(MakeLabel("Claude CLI path:"));
        aiLayout.Controls.Add(_txtClaudePath);

        _btnClaudeDetect = new System.Windows.Forms.Button
        {
            Text = "Auto-detect",
            Width = 110,
            FlatStyle = System.Windows.Forms.FlatStyle.Flat,
            BackColor = System.Drawing.Color.FromArgb(50, 80, 120),
            ForeColor = System.Drawing.Color.White
        };
        _btnClaudeDetect.Click += new System.EventHandler(BtnClaudeDetect_Click);
        aiLayout.Controls.Add(MakeLabel(""));
        aiLayout.Controls.Add(_btnClaudeDetect);

        _chkOpenRouterEnabled = new System.Windows.Forms.CheckBox
        {
            Text = "OpenRouter (HTTP)",
            AutoSize = true,
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235)
        };
        aiLayout.Controls.Add(MakeLabel(""));
        aiLayout.Controls.Add(_chkOpenRouterEnabled);

        _txtOpenRouterKey = MakeTextBox(password: true);
        aiLayout.Controls.Add(MakeLabel("OpenRouter API key:"));
        aiLayout.Controls.Add(_txtOpenRouterKey);

        _txtOpenRouterModel = MakeTextBox();
        aiLayout.Controls.Add(MakeLabel("OpenRouter model:"));
        aiLayout.Controls.Add(_txtOpenRouterModel);

        _lstAiTemplates = new System.Windows.Forms.ListBox
        {
            Height = 80,
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(36, 36, 52),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235)
        };
        _lstAiTemplates.SelectedIndexChanged += new System.EventHandler(LstAiTemplates_SelectedIndexChanged);
        aiLayout.Controls.Add(MakeLabel("Templates:"));
        aiLayout.Controls.Add(_lstAiTemplates);

        _txtTemplateHeaders = MakeTextBox();
        aiLayout.Controls.Add(MakeLabel("Required headers:"));
        aiLayout.Controls.Add(_txtTemplateHeaders);

        _txtTemplateBody = new System.Windows.Forms.TextBox
        {
            Multiline = true,
            Dock = System.Windows.Forms.DockStyle.Fill,
            Height = 160,
            ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
            Font = new System.Drawing.Font("Consolas", 8.5f),
            BackColor = System.Drawing.Color.FromArgb(42, 42, 62),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235)
        };
        aiLayout.Controls.Add(MakeLabel("Template body:"));
        aiLayout.Controls.Add(_txtTemplateBody);

        _tabAi.Controls.Add(aiLayout);

        // ── TabControl ────────────────────────────────────────────────────────
        _tabs.Dock = System.Windows.Forms.DockStyle.Fill;
        _tabs.Appearance = System.Windows.Forms.TabAppearance.FlatButtons;
        _tabs.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _tabs.TabPages.AddRange(new System.Windows.Forms.TabPage[]
        {
            _tabConnection,
            _tabPolling,
            _tabIdentities,
            _tabInboxes,
            _tabBoard,
            _tabNotifications,
            _tabAdvanced,
            _tabAi
        });

        // ── Save button ───────────────────────────────────────────────────────
        _btnSave.Text = "Save";
        _btnSave.Dock = System.Windows.Forms.DockStyle.Bottom;
        _btnSave.Height = 36;
        _btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnSave.BackColor = System.Drawing.Color.FromArgb(60, 100, 160);
        _btnSave.ForeColor = System.Drawing.Color.White;
        _btnSave.FlatAppearance.BorderSize = 0;
        _btnSave.Click += new System.EventHandler(BtnSave_Click);

        // ── Form ──────────────────────────────────────────────────────────────
        Text = "DevPulse \u2014 Settings";
        ClientSize = new System.Drawing.Size(780, 580);
        MinimumSize = new System.Drawing.Size(700, 500);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

        Controls.Add(_tabs);
        Controls.Add(_btnSave);

        _layoutConnection.ResumeLayout(false);
        _layoutConnection.PerformLayout();
        _layoutPolling.ResumeLayout(false);
        _layoutPolling.PerformLayout();
        _layoutIdentities.ResumeLayout(false);
        _layoutIdentities.PerformLayout();
        _layoutBoard.ResumeLayout(false);
        _layoutBoard.PerformLayout();
        _layoutAdvanced.ResumeLayout(false);
        _layoutAdvanced.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_aliasGrid).EndInit();
        ((System.ComponentModel.ISupportInitialize)_columnsGrid).EndInit();
        ((System.ComponentModel.ISupportInitialize)_prInterval).EndInit();
        ((System.ComponentModel.ISupportInitialize)_wiInterval).EndInit();
        _splitInboxes.Panel1.ResumeLayout(false);
        _splitInboxes.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)_splitInboxes).EndInit();
        _splitInboxes.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
