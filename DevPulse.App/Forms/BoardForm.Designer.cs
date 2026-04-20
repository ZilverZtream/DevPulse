namespace DevPulse.App.Forms;

partial class BoardForm
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label _staleBanner;
    private System.Windows.Forms.Panel _toolbar;
    private System.Windows.Forms.FlowLayoutPanel _toolbarFlow;
    private System.Windows.Forms.Panel _boardPanel;
    private System.Windows.Forms.TextBox _searchBox;
    private System.Windows.Forms.ComboBox _typeFilter;
    private System.Windows.Forms.ComboBox _assigneeFilter;
    private System.Windows.Forms.ComboBox _priorityFilter;
    private System.Windows.Forms.Button _btnMineOnly;
    private System.Windows.Forms.Button _btnSprintOnly;
    private System.Windows.Forms.Button _btnBugsOnly;
    private System.Windows.Forms.Button _btnUnassignedOnly;
    private System.Windows.Forms.Button _btnRefresh;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _staleBanner = new System.Windows.Forms.Label();
        _toolbar = new System.Windows.Forms.Panel();
        _toolbarFlow = new System.Windows.Forms.FlowLayoutPanel();
        _boardPanel = new System.Windows.Forms.Panel();
        _searchBox = new System.Windows.Forms.TextBox();
        _typeFilter = new System.Windows.Forms.ComboBox();
        _assigneeFilter = new System.Windows.Forms.ComboBox();
        _priorityFilter = new System.Windows.Forms.ComboBox();
        _btnMineOnly = new System.Windows.Forms.Button();
        _btnSprintOnly = new System.Windows.Forms.Button();
        _btnBugsOnly = new System.Windows.Forms.Button();
        _btnUnassignedOnly = new System.Windows.Forms.Button();
        _btnRefresh = new System.Windows.Forms.Button();
        _toolbar.SuspendLayout();
        _toolbarFlow.SuspendLayout();
        SuspendLayout();
        //
        // _staleBanner
        //
        _staleBanner.Text = "⚠  Board data may be stale — last refresh failed";
        _staleBanner.Dock = System.Windows.Forms.DockStyle.Top;
        _staleBanner.Height = 28;
        _staleBanner.BackColor = System.Drawing.Color.FromArgb(120, 60, 30);
        _staleBanner.ForeColor = System.Drawing.Color.White;
        _staleBanner.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        _staleBanner.Visible = false;
        //
        // _searchBox
        //
        _searchBox.PlaceholderText = "Filter items...";
        _searchBox.Width = 220;
        _searchBox.BackColor = System.Drawing.Color.FromArgb(42, 42, 60);
        _searchBox.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _searchBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        _searchBox.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _searchBox.TextChanged += new System.EventHandler(SearchBox_TextChanged);
        //
        // _typeFilter
        //
        _typeFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        _typeFilter.Width = 130;
        _typeFilter.BackColor = System.Drawing.Color.FromArgb(42, 42, 60);
        _typeFilter.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _typeFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _typeFilter.Items.AddRange(new object[] { "All types", "Feature", "Bug", "Task", "User Story" });
        _typeFilter.SelectedIndex = 0;
        _typeFilter.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _typeFilter.SelectedIndexChanged += new System.EventHandler(TypeFilter_SelectedIndexChanged);
        //
        // _assigneeFilter
        //
        _assigneeFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        _assigneeFilter.Width = 130;
        _assigneeFilter.BackColor = System.Drawing.Color.FromArgb(42, 42, 60);
        _assigneeFilter.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _assigneeFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _assigneeFilter.Items.Add("All assignees");
        _assigneeFilter.SelectedIndex = 0;
        _assigneeFilter.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _assigneeFilter.SelectedIndexChanged += new System.EventHandler(AssigneeFilter_SelectedIndexChanged);
        //
        // _priorityFilter
        //
        _priorityFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        _priorityFilter.Width = 130;
        _priorityFilter.BackColor = System.Drawing.Color.FromArgb(42, 42, 60);
        _priorityFilter.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _priorityFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _priorityFilter.Items.AddRange(new object[] { "All priorities", "P1", "P2", "P3" });
        _priorityFilter.SelectedIndex = 0;
        _priorityFilter.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _priorityFilter.SelectedIndexChanged += new System.EventHandler(PriorityFilter_SelectedIndexChanged);
        //
        // _btnMineOnly
        //
        _btnMineOnly.Text = "Mine only";
        _btnMineOnly.Height = 26;
        _btnMineOnly.AutoSize = true;
        _btnMineOnly.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnMineOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
        _btnMineOnly.ForeColor = System.Drawing.Color.FromArgb(200, 200, 220);
        _btnMineOnly.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnMineOnly.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _btnMineOnly.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnMineOnly.Click += new System.EventHandler(BtnMineOnly_Click);
        //
        // _btnSprintOnly
        //
        _btnSprintOnly.Text = "Current sprint";
        _btnSprintOnly.Height = 26;
        _btnSprintOnly.AutoSize = true;
        _btnSprintOnly.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnSprintOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
        _btnSprintOnly.ForeColor = System.Drawing.Color.FromArgb(200, 200, 220);
        _btnSprintOnly.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnSprintOnly.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _btnSprintOnly.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnSprintOnly.Click += new System.EventHandler(BtnSprintOnly_Click);
        //
        // _btnBugsOnly
        //
        _btnBugsOnly.Text = "Bugs only";
        _btnBugsOnly.Height = 26;
        _btnBugsOnly.AutoSize = true;
        _btnBugsOnly.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnBugsOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
        _btnBugsOnly.ForeColor = System.Drawing.Color.FromArgb(200, 200, 220);
        _btnBugsOnly.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnBugsOnly.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _btnBugsOnly.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnBugsOnly.Click += new System.EventHandler(BtnBugsOnly_Click);
        //
        // _btnUnassignedOnly
        //
        _btnUnassignedOnly.Text = "Unassigned only";
        _btnUnassignedOnly.Height = 26;
        _btnUnassignedOnly.AutoSize = true;
        _btnUnassignedOnly.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnUnassignedOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
        _btnUnassignedOnly.ForeColor = System.Drawing.Color.FromArgb(200, 200, 220);
        _btnUnassignedOnly.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnUnassignedOnly.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _btnUnassignedOnly.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnUnassignedOnly.Click += new System.EventHandler(BtnUnassignedOnly_Click);
        //
        // _btnRefresh
        //
        _btnRefresh.Text = "⟳ Refresh";
        _btnRefresh.Height = 26;
        _btnRefresh.AutoSize = true;
        _btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnRefresh.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
        _btnRefresh.ForeColor = System.Drawing.Color.FromArgb(200, 200, 220);
        _btnRefresh.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnRefresh.Margin = new System.Windows.Forms.Padding(4, 4, 4, 0);
        _btnRefresh.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnRefresh.Click += new System.EventHandler(BtnRefresh_Click);
        //
        // _toolbarFlow
        //
        _toolbarFlow.Dock = System.Windows.Forms.DockStyle.Fill;
        _toolbarFlow.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        _toolbarFlow.WrapContents = false;
        _toolbarFlow.BackColor = System.Drawing.Color.FromArgb(36, 36, 52);
        _toolbarFlow.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
        _toolbarFlow.Controls.Add(_searchBox);
        _toolbarFlow.Controls.Add(_typeFilter);
        _toolbarFlow.Controls.Add(_assigneeFilter);
        _toolbarFlow.Controls.Add(_priorityFilter);
        _toolbarFlow.Controls.Add(_btnMineOnly);
        _toolbarFlow.Controls.Add(_btnSprintOnly);
        _toolbarFlow.Controls.Add(_btnBugsOnly);
        _toolbarFlow.Controls.Add(_btnUnassignedOnly);
        _toolbarFlow.Controls.Add(_btnRefresh);
        //
        // _toolbar
        //
        _toolbar.Height = 42;
        _toolbar.Dock = System.Windows.Forms.DockStyle.Top;
        _toolbar.BackColor = System.Drawing.Color.FromArgb(36, 36, 52);
        _toolbar.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
        _toolbar.Controls.Add(_toolbarFlow);
        //
        // _boardPanel
        //
        _boardPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        _boardPanel.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _boardPanel.AutoScroll = true;
        _boardPanel.Padding = new System.Windows.Forms.Padding(8);
        //
        // BoardForm
        //
        ClientSize = new System.Drawing.Size(1200, 720);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        Text = "DevPulse — Board";
        Controls.Add(_boardPanel);
        Controls.Add(_toolbar);
        Controls.Add(_staleBanner);
        _toolbarFlow.ResumeLayout(false);
        _toolbarFlow.PerformLayout();
        _toolbar.ResumeLayout(false);
        ResumeLayout(false);
    }
}
