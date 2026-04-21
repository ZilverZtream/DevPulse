namespace DevPulse.App.Forms;

partial class AiReviewForm
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label _lblStatus;
    private System.Windows.Forms.ListBox _lstHistory;
    private System.Windows.Forms.RichTextBox _rtfSpec;
    private System.Windows.Forms.Label _lblMetadata;
    private System.Windows.Forms.Button _btnRegenerate;
    private System.Windows.Forms.Button _btnCopy;
    private System.Windows.Forms.Button _btnOpenFolder;
    private System.Windows.Forms.Button _btnClose;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _lblStatus = new System.Windows.Forms.Label();
        _lstHistory = new System.Windows.Forms.ListBox();
        _rtfSpec = new System.Windows.Forms.RichTextBox();
        _lblMetadata = new System.Windows.Forms.Label();
        _btnRegenerate = new System.Windows.Forms.Button();
        _btnCopy = new System.Windows.Forms.Button();
        _btnOpenFolder = new System.Windows.Forms.Button();
        _btnClose = new System.Windows.Forms.Button();

        SuspendLayout();

        _lblStatus.Dock = System.Windows.Forms.DockStyle.Top;
        _lblStatus.Height = 40;
        _lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        _lblStatus.Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold);

        _lstHistory.Dock = System.Windows.Forms.DockStyle.Left;
        _lstHistory.Width = 220;
        _lstHistory.BackColor = System.Drawing.Color.FromArgb(36, 36, 52);
        _lstHistory.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _lstHistory.BorderStyle = System.Windows.Forms.BorderStyle.None;
        _lstHistory.SelectedIndexChanged += new System.EventHandler(LstHistory_SelectedIndexChanged);

        _rtfSpec.Dock = System.Windows.Forms.DockStyle.Fill;
        _rtfSpec.ReadOnly = true;
        _rtfSpec.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _rtfSpec.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _rtfSpec.BorderStyle = System.Windows.Forms.BorderStyle.None;

        _lblMetadata.Dock = System.Windows.Forms.DockStyle.Right;
        _lblMetadata.Width = 220;
        _lblMetadata.Padding = new System.Windows.Forms.Padding(8);
        _lblMetadata.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblMetadata.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);

        var toolbar = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Bottom,
            Height = 40,
            BackColor = System.Drawing.Color.FromArgb(36, 36, 52),
            Padding = new System.Windows.Forms.Padding(8, 6, 8, 6)
        };

        _btnRegenerate.Text = "Regenerate";
        _btnRegenerate.Width = 100;
        _btnRegenerate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnRegenerate.BackColor = System.Drawing.Color.FromArgb(60, 100, 160);
        _btnRegenerate.ForeColor = System.Drawing.Color.White;
        _btnRegenerate.Click += new System.EventHandler(BtnRegenerate_Click);

        _btnCopy.Text = "Copy markdown";
        _btnCopy.Width = 120;
        _btnCopy.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnCopy.Click += new System.EventHandler(BtnCopy_Click);

        _btnOpenFolder.Text = "Open folder";
        _btnOpenFolder.Width = 100;
        _btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnOpenFolder.Click += new System.EventHandler(BtnOpenFolder_Click);

        _btnClose.Text = "Close";
        _btnClose.Width = 80;
        _btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnClose.Click += (_, _) => Close();

        toolbar.Controls.Add(_btnRegenerate);
        toolbar.Controls.Add(_btnCopy);
        toolbar.Controls.Add(_btnOpenFolder);
        toolbar.Controls.Add(_btnClose);

        ClientSize = new System.Drawing.Size(900, 700);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        Font = new System.Drawing.Font("Segoe UI", 9f);
        Text = "DevPulse — AI spec review";
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;

        Controls.Add(_rtfSpec);
        Controls.Add(_lstHistory);
        Controls.Add(_lblMetadata);
        Controls.Add(_lblStatus);
        Controls.Add(toolbar);

        ResumeLayout(false);
    }
}
