using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MonolithVpnClient.Views;

public enum ToastKind { Info, Success, Error }

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _dismissTimer;

    public ToastWindow(string title, string message, ToastKind kind)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        AccentBar.Background = kind switch
        {
            ToastKind.Success => (System.Windows.Media.Brush)FindResource("Green"),
            ToastKind.Error => (System.Windows.Media.Brush)FindResource("Red"),
            _ => (System.Windows.Media.Brush)FindResource("TextLow"),
        };

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _dismissTimer.Tick += (_, _) => Dismiss();

        Loaded += (_, _) => AnimateIn();
    }

    private void AnimateIn()
    {
        var slideIn = new DoubleAnimation(Left + 24, Left, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        BeginAnimation(LeftProperty, slideIn);
        BeginAnimation(OpacityProperty, fadeIn);
        _dismissTimer.Start();
    }

    public void Dismiss()
    {
        _dismissTimer.Stop();
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Dismiss();
    }
}
