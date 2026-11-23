using AmbientTheme.Core.Connection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QRCoder;

namespace AmbientTheme.App;

/// <summary>Tray and window icon rendered from the vector logo at runtime (the .exe icon is Assets/app.ico).</summary>
internal static class AppIcon
{
    private const int Pixels = 64;

    public static WindowIcon Create()
    {
        var logo = new LogoMark { Width = Pixels, Height = Pixels };
        logo.Measure(new Size(Pixels, Pixels));
        logo.Arrange(new Rect(0, 0, Pixels, Pixels));
        var bitmap = new RenderTargetBitmap(new PixelSize(Pixels, Pixels));
        bitmap.Render(logo);
        return new WindowIcon(bitmap);
    }
}

/// <summary>Small builders shared by the code-only windows, so they match the main window's style.</summary>
internal static class Ui
{
    public static Button Button(string text, Action action, bool accent = false)
    {
        var button = new Button { Content = text };
        button.Classes.Set("accent", accent);
        button.Click += (_, _) => action();
        return button;
    }

    public static StackPanel Buttons(params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.AddRange(buttons);
        return row;
    }

    public static TextBlock Text(string text, string? styleClass = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (styleClass is not null)
        {
            block.Classes.Add(styleClass);
        }

        return block;
    }
}

/// <summary>Discreet, non-activating toast in the corner of the primary screen. Shown once per episode.</summary>
internal sealed class NotificationWindow : Window
{
    public NotificationWindow(string message, Action? resume)
    {
        Title = "Ambient Theme Controller";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var buttons = resume is null
            ? Ui.Buttons(Ui.Button("Fechar", Close))
            : Ui.Buttons(Ui.Button("Fechar", Close), Ui.Button("Retomar automático", () =>
            {
                resume();
                Close();
            }, accent: true));
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { new LogoMark { Width = 22, Height = 22 }, Ui.Text("Ambient Theme", "h2") },
        };
        Content = new Border
        {
            Classes = { "card" },
            Child = new StackPanel { Spacing = 10, Children = { header, Ui.Text(message), buttons } },
        };

        Opened += (_, _) =>
        {
            if (Screens.Primary is { } screen)
            {
                var size = PixelSize.FromSize(ClientSize, screen.Scaling);
                Position = new PixelPoint(screen.WorkingArea.Right - size.Width - 24, screen.WorkingArea.Bottom - size.Height - 64);
            }

            DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(15));
        };
    }
}

/// <summary>Yes/no question used for the fingerprint comparison and for destructive choices.</summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string? code, string accept)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(Ui.Text(title, "h1"));
        panel.Children.Add(Ui.Text(message));
        if (code is not null)
        {
            panel.Children.Add(new Border { Classes = { "card" }, Child = Ui.Text(code, "code") });
        }

        panel.Children.Add(Ui.Buttons(Ui.Button("Cancelar", () => Close(false)), Ui.Button(accept, () => Close(true), accent: true)));
        Content = panel;
    }

    public static Task<bool> AskAsync(Window owner, string title, string message, string accept, string? code = null) =>
        new ConfirmDialog(title, message, code, accept).ShowDialog<bool>(owner);
}

/// <summary>
/// Pairing: Windows shows a QR with its identity and a short-lived invite; the phone scans it; Windows then
/// connects to the address typed by the user and asks them to compare the certificate code on both screens.
/// </summary>
internal sealed class PairingWindow : Window
{
    private readonly AppHost _host;
    private readonly Endpoint _endpoint;
    private readonly PairingInvite _invite;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _continue = new()
    {
        Content = "Já escaneei no celular — continuar",
        Classes = { "accent" },
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    public PairingWindow(AppHost host, Endpoint endpoint)
    {
        _host = host;
        _endpoint = endpoint;
        _invite = PairingInvite.Create(host.Settings.Current.ClientId, Environment.MachineName, DateTimeOffset.UtcNow);

        Title = "Parear celular";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _continue.Click += async (_, _) => await PairAsync();
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                Ui.Text("Parear celular", "h1"),
                Ui.Text("1. No celular, abra o Ambient Theme Controller e inicie a sessão de sensor.\n" +
                        "2. Toque em \"Autorizar computador\" e aponte a câmera para este código.\n" +
                        $"3. Volte aqui e continue. O convite vale por {PairingInvite.Lifetime.TotalMinutes:0} minutos e só pode ser usado uma vez."),
                new Border
                {
                    // QR codes need a light quiet zone, whatever the app theme.
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(12),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new Image { Source = QrCode(_invite.ToUri()), Width = 240, Height = 240 },
                },
                Ui.Text($"O código não contém endereço. A conexão será feita em {_endpoint}.", "caption"),
                new TextBox { Text = _invite.ToUri(), IsReadOnly = true, FontSize = 10 },
                _continue,
                _status,
            },
        };
    }

    private static Bitmap QrCode(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var stream = new MemoryStream(new PngByteQRCode(data).GetGraphic(8));
        return new Bitmap(stream);
    }

    private async Task PairAsync()
    {
        if (_invite.IsExpired(DateTimeOffset.UtcNow))
        {
            _status.Text = "O convite expirou. Feche esta janela e comece de novo.";
            _continue.IsEnabled = false;
            return;
        }

        _continue.IsEnabled = false;
        _status.Text = $"Conectando a {_endpoint}…";
        var result = await _host.PairAsync(_endpoint, _invite, code => ConfirmDialog.AskAsync(this,
            "Conferir identidade do celular",
            "Compare este código com o que aparece no celular. Continue somente se forem idênticos.",
            "Os códigos são iguais", code));

        if (result.Failure is null)
        {
            _host.CompletePairing(result, _endpoint);
            Close();
            return;
        }

        _status.Text = result.Failure switch
        {
            ConnectionFailure.TlsIdentityMismatch => "Pareamento cancelado: os códigos não foram confirmados. Nada foi enviado ao celular.",
            ConnectionFailure.AuthFailed => "O celular não reconheceu o convite. Escaneie o código no celular antes de continuar.",
            var failure => $"Não foi possível parear em {_endpoint}: " + Core.Presentation.StatusPresenter.Describe(failure.Value)
                + (Core.Connection.NetworkHints.Describe(_endpoint, Core.Connection.NetworkHints.LocalNetworks()) is { } hint ? "\n\n" + hint : string.Empty),
        };
        _continue.IsEnabled = true;
    }
}
