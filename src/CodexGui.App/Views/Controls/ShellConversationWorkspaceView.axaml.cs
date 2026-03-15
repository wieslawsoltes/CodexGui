using Avalonia.Controls;

namespace CodexGui.App.Views.Controls;

public partial class ShellConversationWorkspaceView : UserControl
{
    public ShellConversationWorkspaceView()
    {
        InitializeComponent();
    }

    public DataGrid ConversationDataGrid => ConversationList;
}
