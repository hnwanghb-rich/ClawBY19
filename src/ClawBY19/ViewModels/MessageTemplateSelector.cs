using ClawBY19.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace ClawBY19.ViewModels;

/// <summary>根据 Role 选择消息模板</summary>
public class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ChatMessageItem msg
            ? msg.IsUser ? UserTemplate : AssistantTemplate
            : base.SelectTemplate(item, container);
}
