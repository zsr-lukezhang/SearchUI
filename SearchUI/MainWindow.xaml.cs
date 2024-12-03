using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;

namespace SearchUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        }

        private async void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            string fileName = FileNameTextBox.Text;
            if (!string.IsNullOrEmpty(fileName))
            {
                SetControlsEnabled(false);
                ResultsListView.Items.Clear();
                ProgressRing.IsActive = true;
                var files = await SearchAsFileName(fileName);
                ProgressRing.IsActive = false;
                foreach (var file in files)
                {
                    ResultsListView.Items.Add(file);
                }
                SetControlsEnabled(true);
                GC.Collect(); // Clean Memory
            }
        }

        private Task<List<FileItem>> SearchAsFileName(string fileName)
        {
            return Task.Run(() =>
            {
                var result = new ConcurrentBag<FileItem>();
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();

                Parallel.ForEach(drives, drive =>
                {
                    try
                    {
                        SearchDirectory(drive.RootDirectory, fileName, result);
                    }
                    catch (Exception ex)
                    {
                        // Log other exceptions if necessary
                        Debug.WriteLine($"Error accessing {drive.RootDirectory.FullName}: {ex.Message}");
                    }
                });

                return result.ToList();
            });
        }

        private void SearchDirectory(DirectoryInfo directory, string fileName, ConcurrentBag<FileItem> result)
        {
            try
            {
                var files = directory.GetFiles("*" + fileName + "*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    result.Add(new FileItem
                    {
                        Icon = "ms-appx:///Assets/FileIcon.png", // Will do it later...
                        Name = file.Name,
                        FullPath = file.FullName,
                        ModifiedTime = file.LastWriteTime.ToString()
                    });
                }

                var subDirectories = directory.GetDirectories();
                Parallel.ForEach(subDirectories, subDirectory =>
                {
                    SearchDirectory(subDirectory, fileName, result);
                });
            }
            catch (UnauthorizedAccessException)
            {
                // Skip unauthorized access folders
                Debug.WriteLine($"Unauthorized access to {directory.FullName}");
            }
            catch (Exception ex)
            {
                // Log other exceptions if necessary
                Debug.WriteLine($"Error accessing {directory.FullName}: {ex.Message}");
            }
        }

        private void SetControlsEnabled(bool isEnabled)
        {
            FileNameTextBox.IsEnabled = isEnabled;
            SearchButton.IsEnabled = isEnabled;
            ResultsListView.IsEnabled = isEnabled;
        }

        private void ResultsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ResultsListView.SelectedItem is FileItem fileItem)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(fileItem.FullPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error opening file {fileItem.FullPath}: {ex.Message}");
                }
            }
        }
        

        /// <summary>
        /// 右键菜单
        /// 就是修不好了
        /// </summary>
        private void ResultsListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (ResultsListView != null)
            {
                // 获取被点击的项
                var element = e.OriginalSource as FrameworkElement;
                if (element != null)
                {
                    var item = element.DataContext;
                    if (item != null)
                    {
                        // 遍历所有项以找到对应的容器
                        foreach (var listViewItem in ResultsListView.Items)
                        {
                            var container = ResultsListView.ContainerFromItem(listViewItem) as ListViewItem;
                            if (container != null && container.Content == item)
                            {
                                // 选中该项
                                ResultsListView.SelectedItem = container.Content;
                                break;
                            }
                        }

                        if (ResultsListView.SelectedItem is FileItem fileItem)
                        {
                            ShowContextMenu(fileItem.FullPath, e.GetPosition(ResultsListView));
                        }
                        else
                        {
                            Debug.WriteLine("SelectedItem is not a FileItem.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Item is null.");
                    }
                }
                else
                {
                    Debug.WriteLine("Element is null.");
                }
            }
            else
            {
                Debug.WriteLine("ResultsListView is null.");
            }
        }
        private void ShowContextMenu(string filePath, Point position)
        {
            var menu = new MenuFlyout();

            var openItem = new MenuFlyoutItem { Text = "Open" };
            openItem.Click += (s, e) => Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

            var showInExplorerItem = new MenuFlyoutItem { Text = "Show in File Explorer" };
            showInExplorerItem.Click += (s, e) => ShowInFileExplorer(filePath);

            var copyAsPathItem = new MenuFlyoutItem { Text = "Copy as full path" };
            copyAsPathItem.Click += (s, e) => CopyFullPathToClipboard();

            menu.Items.Add(openItem);
            menu.Items.Add(showInExplorerItem);
            menu.Items.Add(copyAsPathItem);

            menu.ShowAt(ResultsListView, position);
        }

        private void ShowInFileExplorer(string filePath)
        {
            var argument = $"/select, \"{filePath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", argument) { CreateNoWindow = true });
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                OnSearchButtonClick(sender, null);
            }
        }

        private void CopyFullPathToClipboard()
        {
            if (ResultsListView.SelectedItem is FileItem fileItem)
            {
                CopyTextToClipboard(fileItem.FullPath);
            }
        }

        public Task CopyTextToClipboard(string text)
        {
            // 创建一个 DataPackage 对象
            var dataPackage = new DataPackage();

            // 设置 DataPackage 的文本内容
            dataPackage.SetText(text);

            // 将 DataPackage 设置到剪贴板
            Clipboard.SetContent(dataPackage);

            return Task.CompletedTask;
        }

    }

    public class FileItem
    {
        public string Icon { get; set; }
        public string Name { get; set; }
        public string FullPath { get; set; }
        public string ModifiedTime { get; set; }
    }
}