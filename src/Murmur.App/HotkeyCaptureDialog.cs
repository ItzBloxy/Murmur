using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Murmur.Platform;

namespace Murmur.App;

/// <summary>
/// Captures the next key pressed and returns its <see cref="Keys"/> name. Modifier keys are
/// accepted as bindings in their own right, which is why the default is Right Ctrl.
/// </summary>
public sealed partial class HotkeyCaptureDialog : Form
{
    private readonly Label _prompt;

    public HotkeyCaptureDialog(string currentHotkey)
    {
        Text = "Set hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 130);
        KeyPreview = true;

        _prompt = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10f),
            Text = $"Press the key you want to use.\n\nCurrent: {currentHotkey}\nEscape cancels.",
        };

        Controls.Add(_prompt);
    }

    /// <summary>The chosen key name, or null if cancelled.</summary>
    public string? SelectedHotkey { get; private set; }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        if (message.Msg is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return base.ProcessCmdKey(ref message, keyData);

        var key = (Keys)message.WParam;

        if (key == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        // A window message reports VK_CONTROL for either Ctrl, so ask the keyboard which
        // side is actually held. Without this, picking Right Ctrl also binds Left Ctrl.
        SelectedHotkey = HotkeySwallowRule.ResolveSide(key, IsPhysicallyDown).ToString();

        DialogResult = DialogResult.OK;
        Close();
        return true;
    }

    private static bool IsPhysicallyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);
}
