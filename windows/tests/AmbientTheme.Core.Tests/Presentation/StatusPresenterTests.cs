using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Presentation;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests.Presentation;

public class StatusPresenterTests
{
    private static readonly AppSettings Paired = new()
    {
        PairedDevice = new PairedDevice("dev-1", "Pixel", "PIN", []),
    };

    private static CoordinatorStatus Status(RuntimeState state, DesiredMode mode = DesiredMode.Auto,
        PauseReason reason = PauseReason.None, double? lux = null, SensorState? sensor = null,
        Recommendation? recommendation = null, bool applyBlocked = false, bool contrastBlocked = false,
        FatalFailure? fatal = null) =>
        new(state, mode, reason, lux, sensor, recommendation ?? Recommendation.Hold, applyBlocked, contrastBlocked, fatal);

    private static StatusView Present(CoordinatorStatus status, (Endpoint, ConnectionFailure)? failure = null,
        AppSettings? settings = null) => StatusPresenter.Present(status, settings ?? Paired, failure);

    [Fact]
    public void The_three_automation_states_of_the_spec_are_distinct()
    {
        Assert.Equal("Automação ativa", Present(Status(RuntimeState.Active)).Automation);
        Assert.Equal("Sensor indisponível: aguardando recuperação", Present(Status(RuntimeState.Reconnecting)).Automation);
        Assert.Equal("Pausado: tema modificado externamente",
            Present(Status(RuntimeState.Ready, DesiredMode.ManualPause, PauseReason.ExternalThemeChange)).Automation);
    }

    [Fact]
    public void User_pause_is_shown_as_such_even_with_a_healthy_sensor()
    {
        var view = Present(Status(RuntimeState.Ready, DesiredMode.ManualPause, PauseReason.UserPaused, lux: 40));

        Assert.Equal("Pausado por você", view.Automation);
    }

    [Fact]
    public void Write_failure_and_contrast_theme_are_explained()
    {
        Assert.Contains("falha ao aplicar", Present(Status(RuntimeState.Ready, applyBlocked: true)).Automation);
        Assert.Contains("contraste", Present(Status(RuntimeState.Active, contrastBlocked: true)).Automation);
    }

    [Fact]
    public void Recommend_only_never_claims_automation()
    {
        Assert.Equal("Somente recomendar", Present(Status(RuntimeState.Ready, DesiredMode.RecommendOnly)).Automation);
    }

    [Fact]
    public void Unpaired_app_asks_for_pairing()
    {
        var view = Present(Status(RuntimeState.Unpaired), settings: new AppSettings());

        Assert.Equal("Nenhum celular pareado.", view.Network);
        Assert.Contains("pareamento", view.Automation);
    }

    [Fact]
    public void Sensor_and_network_are_reported_separately()
    {
        var view = Present(Status(RuntimeState.WaitingForSensor, sensor: SensorState.Unavailable));

        Assert.Equal("Conectado.", view.Network);
        Assert.Contains("ausente ou indisponível", view.Sensor);
    }

    [Fact]
    public void Waiting_for_a_new_reading_does_not_show_an_old_value()
    {
        Assert.Equal("Aguardando uma leitura nova do sensor.", Present(Status(RuntimeState.WaitingForSensor)).Sensor);
    }

    [Fact]
    public void Stable_reading_shows_lux()
    {
        Assert.Equal("42 lx", Present(Status(RuntimeState.Active, lux: 42.4)).Sensor);
    }

    [Fact]
    public void Timeout_does_not_blame_a_specific_cause()
    {
        var view = Present(Status(RuntimeState.Reconnecting),
            (new Endpoint("10.0.0.2", 8766), ConnectionFailure.Timeout));

        Assert.Contains("10.0.0.2:8766", view.Network);
        Assert.Contains("rota, firewall, isolamento, endereço incorreto ou serviço indisponível", view.Network);
    }

    [Fact]
    public void Revoked_computer_is_told_to_pair_again()
    {
        var view = Present(Status(RuntimeState.Disconnected, fatal: FatalFailure.AuthFailed));

        Assert.Contains("Pareie novamente", view.Identity);
    }

    [Fact]
    public void Identity_mismatch_is_not_presented_as_a_network_problem()
    {
        var view = Present(Status(RuntimeState.Reconnecting),
            (new Endpoint("10.0.0.2", 8766), ConnectionFailure.TlsIdentityMismatch));

        Assert.Contains("outra identidade", view.Identity);
    }

    [Fact]
    public void Verified_identity_names_the_device()
    {
        Assert.Equal("Identidade verificada: Pixel", Present(Status(RuntimeState.Active)).Identity);
    }

    [Fact]
    public void Recommendation_distinguishes_pending_stable_and_cooldown()
    {
        Assert.Equal("Manter o tema atual", Present(Status(RuntimeState.Active)).Recommendation);
        Assert.Contains("aguardando estabilidade", Present(Status(RuntimeState.Active,
            recommendation: new Recommendation(ThemeKind.Dark, false, false))).Recommendation);
        Assert.Equal("Recomendado: tema escuro", Present(Status(RuntimeState.Ready, DesiredMode.RecommendOnly,
            recommendation: new Recommendation(ThemeKind.Dark, true, false))).Recommendation);
        Assert.Contains("intervalo mínimo", Present(Status(RuntimeState.Active,
            recommendation: new Recommendation(ThemeKind.Light, true, true))).Recommendation);
    }

    [Theory]
    [InlineData(NotificationKind.SensorUnavailable, "Sensor Android indisponível. Automação suspensa; tema atual mantido.")]
    [InlineData(NotificationKind.SensorRecovered, "Sensor recuperado. Controle automático retomado.")]
    [InlineData(NotificationKind.ExternalThemeChange,
        "Tema alterado fora do Ambient Theme Controller. Automação pausada até você retomá-la.")]
    public void Notifications_use_the_wording_of_the_spec(NotificationKind kind, string expected)
    {
        Assert.Equal(expected, StatusPresenter.Describe(kind));
    }

    [Fact]
    public void Every_notification_and_failure_has_a_text()
    {
        Assert.All(Enum.GetValues<NotificationKind>(), kind => Assert.NotEmpty(StatusPresenter.Describe(kind)));
        Assert.All(Enum.GetValues<ConnectionFailure>(), failure => Assert.NotEmpty(StatusPresenter.Describe(failure)));
    }

    [Fact]
    public void Preset_names_are_presentable()
    {
        Assert.All(Enum.GetValues<ThresholdPreset>(), preset => Assert.NotEmpty(StatusPresenter.Describe(preset)));
    }
}
