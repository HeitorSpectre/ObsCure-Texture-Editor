using System;
using System.Drawing;
using System.Windows.Forms;

namespace HVTTool;

internal sealed class BatchProgressDialog : Form
{
    private readonly ProgressBar _bar = new();
    private readonly Label _label = new();
    private readonly Button _cancel = new();
    public bool Cancelled { get; private set; }

    public BatchProgressDialog(int total)
    {
        Text = "Batch Reinsert";
        Width = 520;
        Height = 140;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;

        _bar.Location = new Point(12, 40);
        _bar.Size = new Size(484, 22);
        _bar.Maximum = Math.Max(1, total);

        _label.Location = new Point(12, 12);
        _label.Size = new Size(484, 22);
        _label.Text = $"0 / {total}";

        _cancel.Text = "Cancel";
        _cancel.Location = new Point(412, 70);
        _cancel.Size = new Size(84, 26);
        _cancel.Click += (_, _) => { Cancelled = true; _cancel.Enabled = false; _cancel.Text = "Cancelling..."; };

        Controls.Add(_label);
        Controls.Add(_bar);
        Controls.Add(_cancel);
    }

    public void SetProgress(int value, string text)
    {
        _bar.Value = Math.Min(value, _bar.Maximum);
        _label.Text = text;
    }
}
