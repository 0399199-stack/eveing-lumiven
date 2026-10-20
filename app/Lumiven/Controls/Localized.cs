using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lumiven.Controls;

public sealed class Localized : DependencyObject
{
    private static readonly List<WeakReference<Subscription>> Subscriptions = [];
    public static readonly DependencyProperty TextProperty = Register("Text");
    public static readonly DependencyProperty ContentProperty = Register("Content");
    public static readonly DependencyProperty HeaderProperty = Register("Header");
    public static readonly DependencyProperty PlaceholderProperty = Register("Placeholder");
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");
    public static readonly DependencyProperty NameProperty = Register("Name");
    public static readonly DependencyProperty OnContentProperty = Register("OnContent");
    public static readonly DependencyProperty OffContentProperty = Register("OffContent");
    private static readonly DependencyProperty SubscriptionProperty = DependencyProperty.RegisterAttached("Subscription", typeof(object), typeof(Localized), new PropertyMetadata(null));

    static Localized()
    {
        Localizer.LanguageChanged += (_, _) =>
        {
            for (int index = Subscriptions.Count - 1; index >= 0; index--)
                if (Subscriptions[index].TryGetTarget(out var subscription)) subscription.Apply();
                else Subscriptions.RemoveAt(index);
        };
    }

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);
    public static string GetContent(DependencyObject obj) => (string)obj.GetValue(ContentProperty);
    public static void SetContent(DependencyObject obj, string value) => obj.SetValue(ContentProperty, value);
    public static string GetHeader(DependencyObject obj) => (string)obj.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject obj, string value) => obj.SetValue(HeaderProperty, value);
    public static string GetPlaceholder(DependencyObject obj) => (string)obj.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject obj, string value) => obj.SetValue(PlaceholderProperty, value);
    public static string GetToolTip(DependencyObject obj) => (string)obj.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject obj, string value) => obj.SetValue(ToolTipProperty, value);
    public static string GetName(DependencyObject obj) => (string)obj.GetValue(NameProperty);
    public static void SetName(DependencyObject obj, string value) => obj.SetValue(NameProperty, value);
    public static string GetOnContent(DependencyObject obj) => (string)obj.GetValue(OnContentProperty);
    public static void SetOnContent(DependencyObject obj, string value) => obj.SetValue(OnContentProperty, value);
    public static string GetOffContent(DependencyObject obj) => (string)obj.GetValue(OffContentProperty);
    public static void SetOffContent(DependencyObject obj, string value) => obj.SetValue(OffContentProperty, value);

    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(Localized), new PropertyMetadata(null, Changed));
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (element.GetValue(SubscriptionProperty) is not Subscription subscription)
        {
            subscription = new Subscription(element);
            element.SetValue(SubscriptionProperty, subscription);
        }
        subscription.Apply();
    }

    private sealed class Subscription
    {
        private readonly FrameworkElement _element;
        public Subscription(FrameworkElement element)
        {
            _element = element;
            element.Loaded += (_, _) => Apply();
            Subscriptions.Add(new WeakReference<Subscription>(this));
        }
        public void Apply()
        {
            if (_element.GetValue(TextProperty) is string text && _element is TextBlock block) block.Text = Localizer.T(text);
            if (_element.GetValue(ContentProperty) is string content && _element is ContentControl control) control.Content = Localizer.T(content);
            if (_element.GetValue(HeaderProperty) is string header && _element is ToggleSwitch toggle) toggle.Header = Localizer.T(header);
            if (_element.GetValue(PlaceholderProperty) is string placeholder)
            {
                if (_element is TextBox box) box.PlaceholderText = Localizer.T(placeholder);
                if (_element is AutoSuggestBox search) search.PlaceholderText = Localizer.T(placeholder);
            }
            if (_element.GetValue(ToolTipProperty) is string tip) ToolTipService.SetToolTip(_element, Localizer.T(tip));
            if (_element.GetValue(NameProperty) is string name) AutomationProperties.SetName(_element, Localizer.T(name));
            if (_element is ToggleSwitch switchControl)
            {
                if (_element.GetValue(OnContentProperty) is string on) switchControl.OnContent = Localizer.T(on);
                if (_element.GetValue(OffContentProperty) is string off) switchControl.OffContent = Localizer.T(off);
            }
        }
    }
}
