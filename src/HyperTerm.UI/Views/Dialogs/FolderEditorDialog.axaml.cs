using Avalonia.Controls;

namespace HyperTerm.UI.Views.Dialogs;

public sealed partial class FolderEditorDialog : UserControl
{
    public FolderEditorDialog()
    {
        InitializeComponent();
    }

    internal TextBox PathEditor => FolderPathEditor;
}
