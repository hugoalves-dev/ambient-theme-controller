using System.Globalization;
using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Presentation;
using AmbientTheme.Core.Settings;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AmbientTheme.App;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType JsonFile = new("Configurações (JSON)") { Patterns = ["*.json"] };
    private readonly AppHost _host;
    private bool _rendering;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = AppIcon.Create();

        SimulatedBanner.IsVisible = host.AllowSimulated;
        ShowLoadWarning(host.LoadStatus);
        PresetBox.ItemsSource = Enum.GetValues<ThresholdPreset>().Select(StatusPresenter.Describe).ToArray();
        EndpointBox.Text = host.Settings.Current.ManualEndpoint;

        // Closing only hides: the app keeps running in the tray until "Sair".
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        AutoRadio.IsCheckedChanged += (_, _) => OnMode(AutoRadio, () => host.Coordinator.UserSetMode(DesiredMode.Auto));
        RecommendRadio.IsCheckedChanged += (_, _) => OnMode(RecommendRadio, () => host.Coordinator.UserSetMode(DesiredMode.RecommendOnly));
        PauseRadio.IsCheckedChanged += (_, _) => OnMode(PauseRadio, host.Coordinator.UserPause);
        PresetBox.SelectionChanged += (_, _) => OnPresetSelected();
        ApplyThresholdsButton.Click += (_, _) => OnApplyThresholds();
        NotificationsBox.IsCheckedChanged += (_, _) => OnNotificationsChanged();
        SaveEndpointButton.Click += (_, _) => OnSaveEndpoint();
        PairButton.Click += async (_, _) => await OnPairAsync();
        UnpairButton.Click += async (_, _) => await OnUnpairAsync();
        ExportButton.Click += async (_, _) => await OnExportAsync();
        ImportButton.Click += async (_, _) => await OnImportAsync();
    }

    /// <summary>Reflects coordinator status and settings. User edits in progress (text boxes) are left alone.</summary>
    public void Render(StatusView view)
    {
        _rendering = true;
        try
        {
            var settings = _host.Settings.Current;
            NetworkText.Text = view.Network;
            IdentityText.Text = view.Identity;
            SensorText.Text = view.Sensor;
            AutomationText.Text = view.Automation;
            RecommendationText.Text = view.Recommendation;

            AutoRadio.IsChecked = settings.DesiredMode == DesiredMode.Auto;
            RecommendRadio.IsChecked = settings.DesiredMode == DesiredMode.RecommendOnly;
            PauseRadio.IsChecked = settings.DesiredMode == DesiredMode.ManualPause;
            PresetBox.SelectedIndex = (int)settings.Preset;
            NotificationsBox.IsChecked = settings.NotificationsEnabled;

            if (!DarkBox.IsFocused && !LightBox.IsFocused)
            {
                DarkBox.Text = settings.DarkBelowLux.ToString(CultureInfo.CurrentCulture);
                LightBox.Text = settings.LightAboveLux.ToString(CultureInfo.CurrentCulture);
            }

            var paired = settings.PairedDevice is not null;
            PairingText.Text = paired
                ? $"Pareado com {settings.PairedDevice!.Name}. Para revogar este computador, use o aplicativo no celular."
                : "Nenhum celular pareado. Digite o endereço mostrado no celular e clique em Parear.";
            UnpairButton.IsEnabled = paired;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void ShowLoadWarning(SettingsLoadStatus status)
    {
        SettingsWarning.Text = status switch
        {
            SettingsLoadStatus.RecoveredFromCorruption =>
                "O arquivo de configurações estava ilegível. Uma cópia foi guardada e os padrões foram restaurados; a automação não foi reativada sozinha.",
            SettingsLoadStatus.NewerSchema =>
                "As configurações foram gravadas por uma versão mais nova. Esta sessão não vai gravar alterações para não perdê-las.",
            _ => null,
        };
        SettingsWarning.IsVisible = SettingsWarning.Text is not null;
    }

    private void OnMode(RadioButton radio, Action apply)
    {
        if (!_rendering && radio.IsChecked == true)
        {
            apply();
        }
    }

    private void OnPresetSelected()
    {
        if (_rendering || PresetBox.SelectedIndex < 0)
        {
            return;
        }

        var preset = (ThresholdPreset)PresetBox.SelectedIndex;
        if (preset != ThresholdPreset.Personalized)
        {
            _host.Coordinator.UserSetPreset(preset);
        }
    }

    private void OnApplyThresholds()
    {
        var valid = double.TryParse(DarkBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var dark)
                    && double.TryParse(LightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var light)
                    && _host.Coordinator.UserSetThresholds(dark, light);
        ThresholdMessage.Text = valid
            ? "Limiares personalizados aplicados. Entre os dois valores o tema é mantido."
            : "Valores inválidos: use números não negativos, com o limite de escuro menor que o de claro. Nada foi alterado.";
    }

    private void OnNotificationsChanged()
    {
        if (!_rendering)
        {
            var enabled = NotificationsBox.IsChecked == true;
            _host.Settings.Update(settings => settings with { NotificationsEnabled = enabled });
        }
    }

    private bool TryReadEndpoint(out Endpoint endpoint)
    {
        var valid = Endpoint.TryParse(EndpointBox.Text, out endpoint);
        EndpointMessage.IsVisible = !valid;
        EndpointMessage.Text = valid ? null : "Endereço inválido. Use o formato mostrado no celular: IP ou nome, dois-pontos e porta.";
        return valid;
    }

    private void OnSaveEndpoint()
    {
        if (string.IsNullOrWhiteSpace(EndpointBox.Text))
        {
            EndpointMessage.IsVisible = false;
            _host.SetManualEndpoint(null);
        }
        else if (TryReadEndpoint(out var endpoint))
        {
            _host.SetManualEndpoint(endpoint);
        }
    }

    private async Task OnPairAsync()
    {
        if (TryReadEndpoint(out var endpoint))
        {
            await new PairingWindow(_host, endpoint).ShowDialog(this);
        }
    }

    private async Task OnUnpairAsync()
    {
        if (await ConfirmDialog.AskAsync(this, "Esquecer celular",
                "Este computador vai apagar a credencial e deixar de se conectar ao celular. Para usar de novo será preciso parear.",
                "Esquecer"))
        {
            _host.Unpair();
        }
    }

    private async Task OnExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "ambient-theme-settings.json",
            FileTypeChoices = [JsonFile],
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_host.Export());
        BackupMessage.Text = "Configurações exportadas (sem identidade nem credenciais).";
    }

    private async Task OnImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [JsonFile] });
        if (files.Count == 0)
        {
            return;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        BackupMessage.Text = _host.Import(await reader.ReadToEndAsync())
            ? "Configurações importadas. O modo atual e o pareamento não foram alterados."
            : "Arquivo não reconhecido, de versão mais nova ou com valores inválidos. Nada foi alterado.";
    }
}
