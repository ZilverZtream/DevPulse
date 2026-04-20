namespace DevPulse.App.Forms;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    // Connection tab controls
    private System.Windows.Forms.TextBox _orgUrl;
    private System.Windows.Forms.TextBox _project;
    private System.Windows.Forms.TextBox _repoFilter;
    private System.Windows.Forms.TextBox _currentUser;
    private System.Windows.Forms.TextBox _patBox;
    // Polling tab controls
    private System.Windows.Forms.NumericUpDown _prInterval;
    private System.Windows.Forms.NumericUpDown _wiInterval;
    private System.Windows.Forms.CheckBox _refreshOnStartup;
    // Identities tab controls
    private System.Windows.Forms.TextBox _botPatterns;
    private System.Windows.Forms.TextBox _poQaGroup;
    private System.Windows.Forms.DataGridView _aliasGrid;
    // Inboxes tab controls
    private System.Windows.Forms.ListBox _inboxList;
    private System.Windows.Forms.TextBox _inboxRulesJson;
    // Board tab controls
    private System.Windows.Forms.TextBox _areaPath;
    private System.Windows.Forms.TextBox _iterationPath;
    private System.Windows.Forms.DataGridView _columnsGrid;
    // Advanced tab controls
    private System.Windows.Forms.NumericUpDown _maxEvents;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        // Field controls are initialized in BuildUi() which calls the tab builder helpers.
        // InitializeComponent sets only the basic form properties so the VS Designer
        // can open this form without errors.
        ClientSize = new System.Drawing.Size(780, 580);
        MinimumSize = new System.Drawing.Size(700, 500);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Text = "DevPulse — Settings";
        ResumeLayout(false);
    }
}
