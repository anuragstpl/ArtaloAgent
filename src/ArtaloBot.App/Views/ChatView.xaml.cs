using System.Collections.Specialized;
using System.Windows.Controls;

namespace ArtaloBot.App.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (MessagesItemsControl.ItemsSource is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += (_, _) =>
                {
                    MessagesScrollViewer.ScrollToEnd();
                };
            }
        };
    }
}
