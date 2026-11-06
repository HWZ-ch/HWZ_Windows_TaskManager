using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HWZ.TaskManager.ViewModels;

namespace HWZ.TaskManager.Views;

public partial class ProcessesView : UserControl
{
    public ProcessesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (ProcessGrid.Columns.Count > 0)
            {
                ProcessGrid.Columns[0].SortDirection = ListSortDirection.Ascending;
            }
        };
    }

    private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || ProcessGrid.SelectedItem is null)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridRow)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        if (source is DataGridRow && viewModel.ShowDetailsCommand.CanExecute(null))
        {
            viewModel.ShowDetailsCommand.Execute(null);
        }
    }
}
