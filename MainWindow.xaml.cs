using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SL_Cleaning.ViewModels;
using SL_Cleaning.Models;

namespace SL_Cleaning
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Handle row clicks to toggle selection
            SoftwareGrid.LoadingRow += (s, e) =>
            {
                e.Row.PreviewMouseLeftButtonDown += DataGridRow_PreviewMouseLeftButtonDown;
            };
            
            SoftwareGrid.UnloadingRow += (s, e) =>
            {
                e.Row.PreviewMouseLeftButtonDown -= DataGridRow_PreviewMouseLeftButtonDown;
            };
        }

        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is SoftwareEntry entry)
            {
                // Check if the click was on the checkbox cell - if so, don't toggle
                var hitElement = e.OriginalSource as DependencyObject;
                while (hitElement != null)
                {
                    if (hitElement is CheckBox)
                        return; // Let the checkbox handle it
                    
                    if (hitElement == row)
                        break;
                    
                    hitElement = VisualTreeHelper.GetParent(hitElement);
                }

                // Toggle selection via ViewModel command
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.ToggleSelectionCommand.Execute(entry);
                }
            }
        }

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close overlay when clicking on the background (outside the dialog)
            if (DataContext is MainWindowViewModel viewModel && 
                viewModel.IsOverlayVisible &&
                !viewModel.IsUninstalling) // Don't allow closing during uninstall
            {
                viewModel.CloseOverlayCommand.Execute(null);
            }
        }

        private void DisclaimerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close disclaimer when clicking on the background
            if (DataContext is MainWindowViewModel viewModel && 
                viewModel.IsDisclaimerMode)
            {
                viewModel.CloseOverlayCommand.Execute(null);
            }
        }

        private void OverlayContent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent click from bubbling up to the overlay background
            e.Handled = true;
        }
    }
}