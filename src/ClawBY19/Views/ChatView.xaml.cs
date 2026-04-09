using ClawBY19.Services.Chat;
using ClawBY19.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ClawBY19.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ChatViewModel v)
                v.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        };
    }

    private void AttachBtn_Click(object sender, RoutedEventArgs e)
    {
        AttachPopup.IsOpen = true;
    }

    private void AttachMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AttachPopup.IsOpen = false;
    }

    private void CredSubmitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatViewModel vm)
        {
            vm.SetCredentialPassword(CredPwdBox.Password);
            vm.SubmitCredentialCommand.Execute(null);
            CredPwdBox.Clear();
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is ChatViewModel vm && vm.SendMessageCommand.CanExecute(null))
                vm.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+V 粘贴图片
        else if (e.Key == Key.V && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Clipboard.ContainsImage())
            {
                var bmp = Clipboard.GetImage();
                if (bmp is not null)
                    (DataContext as ChatViewModel)?.PasteImage(bmp);
                e.Handled = true;
            }
        }
    }

    private void NickNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is ChatViewModel vm && vm.ConfirmNickNameCommand.CanExecute(null))
                vm.ConfirmNickNameCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 图片右键：从 ContextMenu.PlacementTarget 拿到 Image ──────────

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        var img = GetImageFromMenu(sender);
        if (img?.Source is BitmapSource bmp)
            Clipboard.SetImage(bmp);
    }

    private void CopyImageName_Click(object sender, RoutedEventArgs e)
    {
        var img = GetImageFromMenu(sender);
        if (img?.DataContext is AttachmentItem item)
            Clipboard.SetText(item.DisplayName);
    }

    private void Image_RightClick(object sender, MouseButtonEventArgs e) { /* handled by ContextMenu */ }

    private static Image? GetImageFromMenu(object sender)
    {
        if (sender is MenuItem mi &&
            mi.Parent is ContextMenu cm &&
            cm.PlacementTarget is Image img)
            return img;
        return null;
    }

    private void TestTypeWeb_Click(object sender, RoutedEventArgs e) { /* binding handles state */ }
    private void TestTypeExe_Click(object sender, RoutedEventArgs e) { /* binding handles state */ }

    private void GitUploadSubmitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatViewModel vm)
        {
            vm.SetGitToken(GitTokenBox.Password);
            vm.SubmitGitUploadCommand.Execute(null);
            GitTokenBox.Clear();
        }
    }

    private void TestSubmitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatViewModel vm)
        {
            vm.SetTestPassword(TestPwdBox.Password);
            vm.SubmitTestDialogCommand.Execute(null);
            TestPwdBox.Clear();
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(() => MessageScroller.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
