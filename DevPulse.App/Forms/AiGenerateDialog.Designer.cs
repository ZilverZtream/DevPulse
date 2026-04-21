namespace DevPulse.App.Forms;

partial class AiGenerateDialog
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label _lblHeader;
    private System.Windows.Forms.Label _lblTemplate;
    private System.Windows.Forms.ComboBox _cboTemplate;
    private System.Windows.Forms.Label _lblProvider;
    private System.Windows.Forms.ComboBox _cboProvider;
    private System.Windows.Forms.Label _lblModel;
    private System.Windows.Forms.TextBox _txtModel;
    private System.Windows.Forms.Label _lblWarning;
    private System.Windows.Forms.Button _btnGenerate;
    private System.Windows.Forms.Button _btnCancel;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _lblHeader = new System.Windows.Forms.Label();
        _lblTemplate = new System.Windows.Forms.Label();
        _cboTemplate = new System.Windows.Forms.ComboBox();
        _lblProvider = new System.Windows.Forms.Label();
        _cboProvider = new System.Windows.Forms.ComboBox();
        _lblModel = new System.Windows.Forms.Label();
        _txtModel = new System.Windows.Forms.TextBox();
        _lblWarning = new System.Windows.Forms.Label();
        _btnGenerate = new System.Windows.Forms.Button();
        _btnCancel = new System.Windows.Forms.Button();

        SuspendLayout();

        _lblHeader.Location = new System.Drawing.Point(16, 12);
        _lblHeader.Size = new System.Drawing.Size(380, 40);
        _lblHeader.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _lblHeader.Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold);

        _lblTemplate.Location = new System.Drawing.Point(16, 60);
        _lblTemplate.Size = new System.Drawing.Size(80, 22);
        _lblTemplate.Text = "Template:";
        _lblTemplate.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);

        _cboTemplate.Location = new System.Drawing.Point(100, 58);
        _cboTemplate.Size = new System.Drawing.Size(296, 22);
        _cboTemplate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;

        _lblProvider.Location = new System.Drawing.Point(16, 92);
        _lblProvider.Size = new System.Drawing.Size(80, 22);
        _lblProvider.Text = "Provider:";
        _lblProvider.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);

        _cboProvider.Location = new System.Drawing.Point(100, 90);
        _cboProvider.Size = new System.Drawing.Size(296, 22);
        _cboProvider.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        _cboProvider.SelectedIndexChanged += new System.EventHandler(CboProvider_SelectedIndexChanged);

        _lblModel.Location = new System.Drawing.Point(16, 124);
        _lblModel.Size = new System.Drawing.Size(80, 22);
        _lblModel.Text = "Model:";
        _lblModel.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);

        _txtModel.Location = new System.Drawing.Point(100, 122);
        _txtModel.Size = new System.Drawing.Size(296, 22);

        _lblWarning.Location = new System.Drawing.Point(16, 156);
        _lblWarning.Size = new System.Drawing.Size(380, 40);
        _lblWarning.ForeColor = System.Drawing.Color.FromArgb(220, 120, 120);
        _lblWarning.Visible = false;

        _btnGenerate.Location = new System.Drawing.Point(216, 212);
        _btnGenerate.Size = new System.Drawing.Size(90, 28);
        _btnGenerate.Text = "Generate";
        _btnGenerate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnGenerate.BackColor = System.Drawing.Color.FromArgb(60, 100, 160);
        _btnGenerate.ForeColor = System.Drawing.Color.White;
        _btnGenerate.Click += new System.EventHandler(BtnGenerate_Click);

        _btnCancel.Location = new System.Drawing.Point(312, 212);
        _btnCancel.Size = new System.Drawing.Size(80, 28);
        _btnCancel.Text = "Cancel";
        _btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;

        ClientSize = new System.Drawing.Size(420, 260);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        Font = new System.Drawing.Font("Segoe UI", 9f);
        Text = "DevPulse — Draft spec with AI";
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        AcceptButton = _btnGenerate;
        CancelButton = _btnCancel;

        Controls.Add(_lblHeader);
        Controls.Add(_lblTemplate); Controls.Add(_cboTemplate);
        Controls.Add(_lblProvider); Controls.Add(_cboProvider);
        Controls.Add(_lblModel); Controls.Add(_txtModel);
        Controls.Add(_lblWarning);
        Controls.Add(_btnGenerate); Controls.Add(_btnCancel);

        ResumeLayout(false);
    }
}
