using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.IO;

namespace DuplicateImageCheck
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private string _folderPath = "";

		public MainWindow()
		{
			InitializeComponent();
		}

		private void BrowseButton_Click(object sender, RoutedEventArgs e)
		{
			using var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog();
			dialog.IsFolderPicker = true;

			if (dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
			{
				folderTextBox.Text = dialog.FileName;
				_folderPath = dialog.FileName;
			}
		}

		private async void StartButton_Click(object sender, RoutedEventArgs e)
		{
			var scanner = new ImageScanner();
			scanner.OnStatusChanged += Scanner_OnStatusChanged;

			List<ImageScanner.ImageMatch> matches = await scanner.Process(_folderPath, 80.0);
			matches.Sort((a, b) => (int) (b.Similarity - a.Similarity));
			matchesDataGrid.ItemsSource = matches;
		}

		private void Scanner_OnStatusChanged(string status)
		{
			statusLabel.Dispatcher.Invoke(new Action(() => { statusLabel.Content = "Status: " + status; }));
		}

		private void MatchesDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
		{

		}

		private void DataGridHyperlinkColumn_Click(object sender, RoutedEventArgs e)
		{
			if (e.Source is Hyperlink link)
			{
				var startInfo = new System.Diagnostics.ProcessStartInfo(Path.Combine(_folderPath, link.NavigateUri.OriginalString))
				{
					UseShellExecute = true
				};

				System.Diagnostics.Process.Start(startInfo);
			}
		}
	}
}
