using Avalonia.Controls;

namespace HyperTerm.UI.Views.Dialogs;

public sealed partial class SessionEditorDialog : UserControl
{
    public SessionEditorDialog()
    {
        InitializeComponent();
    }

    internal TextBox NameEditor => SessionNameEditor;
}
