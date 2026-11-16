using AmbientTheme.Core.Connection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QRCoder;

namespace AmbientTheme.App;

/// <summary>Tray and window icon drawn at runtime, so the repository carries no binary asset.</summary>
internal static class AppIcon
{
    public static WindowIcon Create()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawEllipse(Brushes.Gold, null, new Point(32, 32), 28, 28);
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(32, 36, 48)), null, new Point(44, 28), 22, 22);
        }

        return new WindowIcon(bitmap);
    }
}

/// <summary>Discreet, non-activating toast in the corner of the primary screen. Shown once per episode.</summary>
internal sealed class NotificationWindow : Window
{
    public NotificationWindow(string message, Action? resume)
    {
        Title = "Ambient Theme Controller";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (resume is not null)
        {
            buttons.Children.Add(Button("Retomar automático", () =>
            {
                resume();
                Close();
            }));
        }

        buttons.Children.Add(Button("Fechar", Close));
        panel.Children.Add(buttons);
        Content = panel;

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

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }
}

/// <summary>Yes/no question used for the fingerprint comparison and for destructive choices.</summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string? code, string accept)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (code is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = code,
                FontSize = 28,
                FontWeight = FontWeight.Bold,
                FontFamily = new FontFamily("Consolas, monospace"),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var yes = new Button { Content = accept };
        yes.Click += (_, _) => Close(true);
        var no = new Button { Content = "Cancelar" };
        no.Click += (_, _) => Close(false);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { no, yes },
        });
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
    private readonly Button _continue = new() { Content = "Já escaneei no celular — continuar" };

    public PairingWindow(AppHost host, Endpoint endpoint)
    {
        _host = host;
        _endpoint = endpoint;
        _invite = PairingInvite.Create(host.Settings.Current.ClientId, Environment.MachineName, DateTimeOffset.UtcNow);

        Title = "Parear celular";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _continue.Click += async (_, _) => await PairAsync();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "1. No celular, abra o Ambient Theme Controller e inicie a sessão de sensor.\n" +
                           "2. Toque em \"Autorizar computador\" e aponte a câmera para este código.\n" +
                           $"3. Volte aqui e continue. O convite vale por {PairingInvite.Lifetime.TotalMinutes:0} minutos e só pode ser usado uma vez.",
                },
                new Image { Source = QrCode(_invite.ToUri()), Width = 260, Height = 260 },
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Text = $"O código não contém endereço. A conexão será feita em {_endpoint}.",
                },
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
            var failure => $"Não foi possível parear em {_endpoint}: " + Core.Presentation.StatusPresenter.Describe(failure.Value),
        };
        _continue.IsEnabled = true;
    }
}
