using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

// 管理台的一页：从 ManagerWindow.cs 拆出来。页面构建 + 该页辅助逻辑放在一起，
// 改某一页不用在 2800 行里翻。类仍是 partial，共享全部字段和方法。

internal static partial class Program
{
    internal sealed partial class ManagerWindow : Window
    {
        // ---------------- 第 3 页：共享盘同步 ----------------
        private Panel BuildRootsPage()
        {
            StackPanel page = new StackPanel();

            StackPanel card = new StackPanel();
            card.Margin = new Thickness(24, 20, 24, 22);

            TextBlock tip = new TextBlock();
            tip.Text = "添加共享盘根后，程序会定期扫它，自动发现对方新标的文件夹并生成自己的缓存。建议给最深的那一层（例如 ...\\视频素材）。";
            tip.Foreground = WinUi.B("TextSecondary");
            tip.FontSize = 13.5;
            tip.TextWrapping = TextWrapping.Wrap;
            tip.LineHeight = 20;
            tip.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            tip.Margin = new Thickness(0, 0, 0, 14);
            card.Children.Add(tip);

            Border listHost = new Border();
            listHost.Background = WinUi.B("ControlFill");
            listHost.BorderBrush = WinUi.B("ControlStroke");
            listHost.BorderThickness = new Thickness(1);
            listHost.CornerRadius = new CornerRadius(4);
            listHost.Height = 196;

            rootsList = new ListBox();
            Style lbStyle = WinUi.S("WinListBox");
            if (lbStyle != null) { rootsList.Style = lbStyle; }
            rootsList.Padding = new Thickness(0, 4, 0, 4);
            rootsList.ItemTemplate = BuildRootItemTemplate();

            Grid listLayer = new Grid();
            listLayer.Children.Add(rootsList);

            TextBlock emptyRoots = new TextBlock();
            emptyRoots.Text = "还没有添加共享盘根";
            emptyRoots.FontSize = 13;
            emptyRoots.Foreground = WinUi.B("TextTertiary");
            emptyRoots.HorizontalAlignment = HorizontalAlignment.Center;
            emptyRoots.VerticalAlignment = VerticalAlignment.Center;
            emptyRoots.IsHitTestVisible = false;
            emptyRoots.Visibility = Visibility.Collapsed;
            listLayer.Children.Add(emptyRoots);
            rootsEmpty = emptyRoots;

            listHost.Child = listLayer;
            card.Children.Add(listHost);

            Grid row = new Grid();
            row.Margin = new Thickness(0, 14, 0, 0);
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            row.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = GridLength.Auto;
            row.ColumnDefinitions.Add(c1);
            ColumnDefinition c2 = new ColumnDefinition();
            c2.Width = GridLength.Auto;
            row.ColumnDefinitions.Add(c2);

            rootInput = new TextBox();
            Style tbStyle = WinUi.S("WinTextBox");
            if (tbStyle != null) { rootInput.Style = tbStyle; }
            rootInput.Height = 32;
            rootInput.VerticalContentAlignment = VerticalAlignment.Center;
            rootInput.AllowDrop = true;

            ToolTip tipBox = new ToolTip();
            tipBox.Content = "粘贴共享盘路径，或直接把文件夹拖进来";
            Style ttStyle = WinUi.S("WinToolTip");
            if (ttStyle != null) { tipBox.Style = ttStyle; }
            rootInput.ToolTip = tipBox;

            rootInput.Drop += delegate(object sender, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return; }
                string[] items = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (items != null && items.Length > 0) { rootInput.Text = items[0]; }
                // 必须拦住冒泡，否则窗口级的 Drop 还会把同一批文件夹标上色
                e.Handled = true;
            };

            // 占位提示（WPF 的 TextBox 没有 Placeholder，叠一层灰字）
            TextBlock holder = new TextBlock();
            holder.Text = "粘贴共享盘路径，或把文件夹拖进来";
            holder.FontSize = 13;
            holder.Foreground = WinUi.B("TextTertiary");
            holder.Margin = new Thickness(12, 0, 0, 0);
            holder.VerticalAlignment = VerticalAlignment.Center;
            holder.IsHitTestVisible = false;
            holder.Visibility = Visibility.Visible;
            rootInput.TextChanged += delegate
            {
                holder.Visibility = rootInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };

            Grid inputHost = new Grid();
            inputHost.Children.Add(rootInput);
            inputHost.Children.Add(holder);
            Grid.SetColumn(inputHost, 0);
            row.Children.Add(inputHost);

            Button bAdd = MakeButton("WinAccentButton", "添加");
            bAdd.MinWidth = 80;
            bAdd.Margin = new Thickness(8, 0, 0, 0);
            bAdd.VerticalAlignment = VerticalAlignment.Center;
            bAdd.Click += delegate
            {
                string r = rootInput.Text.Trim();
                if (r.Length == 0) { WinUiDialog.Info(this, "缺少路径", "先填一个共享盘路径，或把文件夹拖进来。"); return; }
                try { r = System.IO.Path.GetFullPath(r); }
                catch { }
                AddRoot(r);
                rootInput.Text = "";
                ReloadRoots();
            };
            Grid.SetColumn(bAdd, 1);
            row.Children.Add(bAdd);

            Button bDel = MakeButton("WinButton", "删除选中");
            bDel.MinWidth = 92;
            bDel.Margin = new Thickness(8, 0, 0, 0);
            bDel.VerticalAlignment = VerticalAlignment.Center;
            bDel.Click += delegate
            {
                if (rootsList.SelectedItem != null)
                {
                    RemoveRoot((string)rootsList.SelectedItem);
                    ReloadRoots();
                }
            };
            Grid.SetColumn(bDel, 2);
            row.Children.Add(bDel);

            card.Children.Add(row);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.Margin = new Thickness(0, 16, 0, 0);

            Button bImport = MakeButton("WinButton", "立即导入并生成缓存");
            bImport.MinWidth = 168;
            bImport.Click += delegate
            {
                string r = rootsList.SelectedItem == null ? rootInput.Text.Trim() : (string)rootsList.SelectedItem;
                if (r.Length == 0) { WinUiDialog.Info(this, "缺少路径", "先选中或填一个共享盘根。"); return; }
                RunAsync("import \"" + r + "\"", "导入中…");
            };
            btns.Children.Add(bImport);
            card.Children.Add(btns);

            page.Children.Add(WinUi.Card(card, new Thickness(0)));

            TextBlock note = new TextBlock();
            note.Text = "导入会递归扫该根下的 desktop.ini，并为本机生成缓存 ico；添加后无需再敲命令。";
            note.Foreground = WinUi.B("TextTertiary");
            note.FontSize = WinUi.SizeCaption;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(4, 14, 4, 0);
            page.Children.Add(note);

            return page;
        }

        private DataTemplate BuildRootItemTemplate()
        {
            const string xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <Grid Height='40' Margin='12,0,12,0'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='Auto'/>
      <ColumnDefinition Width='*'/>
    </Grid.ColumnDefinitions>
    <TextBlock Text='&#xE753;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='14'
               Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center' Margin='0,0,10,0'/>
    <TextBlock Grid.Column='1' Text='{Binding}' FontSize='13' Foreground='{DynamicResource TextPrimary}'
               TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/>
  </Grid>
</DataTemplate>";
            try { return (DataTemplate)XamlReader.Parse(xaml); }
            catch { return null; }
        }

        private void ReloadRoots()
        {
            if (rootsList == null) { return; }
            rootsList.Items.Clear();
            foreach (string r in ReadRoots()) { rootsList.Items.Add(r); }
            if (rootsEmpty != null)
            {
                rootsEmpty.Visibility = rootsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

    }
}
