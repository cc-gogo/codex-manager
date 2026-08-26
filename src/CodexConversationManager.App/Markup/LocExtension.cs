using System.Windows.Data;
using System.Windows.Markup;
using CodexConversationManager.App.Services;

namespace CodexConversationManager.App.Markup;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]")
    {
        Source = LanguageManager.Instance,
        Mode = BindingMode.OneWay
    }.ProvideValue(serviceProvider);
}
