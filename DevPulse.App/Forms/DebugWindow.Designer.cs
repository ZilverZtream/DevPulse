namespace DevPulse.App.Forms;

partial class DebugWindow
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.TabControl _tabs;
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
        _tabs = new System.Windows.Forms.TabControl();
        _btnRefresh = new System.Windows.Forms.Button();
        SuspendLayout();
        //
        // _tabs
        //
        _tabs.Dock = System.Windows.Forms.DockStyle.Fill;
        _tabs.Appearance = System.Windows.Forms.TabAppearance.FlatButtons;
        //
        // _btnRefresh
        //
        _btnRefresh.Text = "Refresh";
        _btnRefresh.Dock = System.Windows.Forms.DockStyle.Bottom;
        _btnRefresh.Height = 30;
        _btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnRefresh.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
        _btnRefresh.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _btnRefresh.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(80, 80, 110);
        _btnRefresh.Click += new System.EventHandler(BtnRefresh_Click);
        //
        // DebugWindow
        //
        ClientSize = new System.Drawing.Size(900, 620);
        BackColor = System.Drawing.Color.FromArgb(20, 20, 36);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        Text = "DevPulse \u2014 Debug / Audit";
        Controls.Add(_tabs);
        Controls.Add(_btnRefresh);
        ResumeLayout(false);
    }
}
