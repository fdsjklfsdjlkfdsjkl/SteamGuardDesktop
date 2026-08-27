namespace SteamGuardDesktop;

internal sealed class PromptDialog : Form
{
    private readonly TextBox _textBox;

    private PromptDialog(string title, string message, bool secret)
    {
        Text = title;
        Width = 470;
        Height = 185;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label { Left = 14, Top = 14, Width = 425, Height = 38, Text = message };
        _textBox = new TextBox { Left = 14, Top = 58, Width = 425, UseSystemPasswordChar = secret };
        var ok = new Button { Text = "OK", Left = 278, Top = 95, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 364, Top = 95, Width = 75, DialogResult = DialogResult.Cancel };
        Controls.AddRange([label, _textBox, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Show(IWin32Window owner, string title, string message, bool secret = false)
    {
        using var dialog = new PromptDialog(title, message, secret);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._textBox.Text : null;
    }
}
