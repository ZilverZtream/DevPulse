namespace DevPulse.App.Forms;

partial class InboxEventsForm
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Panel _toolbar;
    private System.Windows.Forms.FlowLayoutPanel _toolbarFlow;
    private System.Windows.Forms.Button _btnMarkAll;
    private System.Windows.Forms.Button _btnRefresh;
    private System.Windows.Forms.ListView _listView;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _toolbar = new System.Windows.Forms.Panel();
        _toolbarFlow = new System.Windows.Forms.FlowLayoutPanel();
        _btnMarkAll = new System.Windows.Forms.Button();
        _btnRefresh = new System.Windows.Forms.Button();
        _listView = new System.Windows.Forms.ListView();
        _toolbar.SuspendLayout();
        _toolbarFlow.SuspendLayout();
        SuspendLayout();
        //
        // _btnMarkAll
        //
        _btnMarkAll.Text = "Mark all as read";
        _btnMarkAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnMarkAll.BackColor = System.Drawing.Color.FromArgb(60, 60, 90);
        _btnMarkAll.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _btnMarkAll.Height = 26;
        _btnMarkAll.AutoSize = true;
        _btnMarkAll.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnMarkAll.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnMarkAll.Click += new System.EventHandler(BtnMarkAll_Click);
        //
        // _btnRefresh
        //
        _btnRefresh.Text = "Refresh";
        _btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnRefresh.BackColor = System.Drawing.Color.FromArgb(60, 60, 90);
        _btnRefresh.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _btnRefresh.Height = 26;
        _btnRefresh.AutoSize = true;
        _btnRefresh.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        _btnRefresh.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnRefresh.Click += new System.EventHandler(BtnRefresh_Click);
        //
        // _toolbarFlow
        //
        _toolbarFlow.Dock = System.Windows.Forms.DockStyle.Fill;
        _toolbarFlow.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        _toolbarFlow.WrapContents = false;
        _toolbarFlow.BackColor = System.Drawing.Color.FromArgb(42, 42, 60);
        _toolbarFlow.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);
        _toolbarFlow.Controls.Add(_btnMarkAll);
        _toolbarFlow.Controls.Add(_btnRefresh);
        //
        // _toolbar
        //
        _toolbar.Height = 36;
        _toolbar.Dock = System.Windows.Forms.DockStyle.Top;
        _toolbar.BackColor = System.Drawing.Color.FromArgb(42, 42, 60);
        _toolbar.Controls.Add(_toolbarFlow);
        //
        // _listView
        //
        _listView.Dock = System.Windows.Forms.DockStyle.Fill;
        _listView.View = System.Windows.Forms.View.Details;
        _listView.FullRowSelect = true;
        _listView.GridLines = false;
        _listView.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _listView.ForeColor = System.Drawing.Color.FromArgb(224, 224, 240);
        _listView.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        _listView.BorderStyle = System.Windows.Forms.BorderStyle.None;
        _listView.Columns.Add("", 24);
        _listView.Columns.Add("PR", 60);
        _listView.Columns.Add("Title", 240);
        _listView.Columns.Add("Author", 160);
        _listView.Columns.Add("Summary", 220);
        _listView.Columns.Add("Time", 110);
        _listView.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(OnDoubleClick);
        _listView.MouseClick += new System.Windows.Forms.MouseEventHandler(OnRightClick);
        //
        // InboxEventsForm
        //
        ClientSize = new System.Drawing.Size(820, 570);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(224, 224, 240);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        Text = "DevPulse — Inbox";
        Controls.Add(_listView);
        Controls.Add(_toolbar);
        _toolbarFlow.ResumeLayout(false);
        _toolbarFlow.PerformLayout();
        _toolbar.ResumeLayout(false);
        ResumeLayout(false);
    }
}
