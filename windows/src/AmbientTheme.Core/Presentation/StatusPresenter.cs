using System.Globalization;
using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Presentation;

/// <summary>Network, identity, sensor and automation are always reported as separate lines (spec §10).</summary>
public sealed record StatusView(string Network, string Identity, string Sensor, string Automation, string Recommendation,
    double? Lux, StatusTone Tone)
{
    public string TrayTooltip => $"Ambient Theme Controller — {Automation}";
}

/// <summary>Colour family of the automation state, so the UI never parses wording.</summary>
public enum StatusTone { Ok, Waiting, Paused, Problem }

/// <summary>All user-facing wording in one place, independent from the UI toolkit.</summary>
public static class StatusPresenter
{
    public static StatusView Present(CoordinatorStatus status, AppSettings settings,
        (Endpoint Endpoint, ConnectionFailure Failure)? lastFailure) =>
        new(Network(status, lastFailure), Identity(status, settings, lastFailure), Sensor(status),
            Automation(status), Recommendation(status.Recommendation), TrustedLux(status), Tone(status));

    private static double? TrustedLux(CoordinatorStatus status) =>
        status.State is RuntimeState.Stabilizing or RuntimeState.Ready or RuntimeState.Active ? status.Lux : null;

    private static StatusTone Tone(CoordinatorStatus status) => status switch
    {
        { Fatal: not null } or { ApplyBlocked: true } => StatusTone.Problem,
        { Mode: DesiredMode.ManualPause or DesiredMode.RecommendOnly } or { ContrastBlocked: true } => StatusTone.Paused,
        { State: RuntimeState.Active } => StatusTone.Ok,
        _ => StatusTone.Waiting,
    };

    public static string Describe(NotificationKind kind) => kind switch
    {
        NotificationKind.SensorUnavailable => "Sensor Android indisponível. Automação suspensa; tema atual mantido.",
        NotificationKind.SensorRecovered => "Sensor recuperado. Controle automático retomado.",
        NotificationKind.ExternalThemeChange =>
            "Tema alterado fora do Ambient Theme Controller. Automação pausada até você retomá-la.",
        NotificationKind.ThemeApplyFailed =>
            "Não foi possível aplicar o tema por completo. Automação suspensa até você retomá-la.",
        NotificationKind.PairingRejected =>
            "O celular recusou este computador. Automação suspensa; pareie novamente para continuar.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Error language of spec §10: say what is known, never guess the culprit.</summary>
    public static string Describe(ConnectionFailure failure) => failure switch
    {
        ConnectionFailure.Refused =>
            "o destino respondeu, mas nada aceitou a conexão nessa porta. Confira se a sessão de sensor está ativa no celular.",
        ConnectionFailure.Timeout =>
            "a conexão não foi concluída. Pode ser rota, firewall, isolamento, endereço incorreto ou serviço indisponível.",
        ConnectionFailure.Unreachable =>
            "o sistema não encontrou caminho até o endereço. Confira se os dois aparelhos estão na mesma rede.",
        ConnectionFailure.TlsIdentityMismatch =>
            "o endereço respondeu com outra identidade.",
        ConnectionFailure.AuthFailed =>
            "o celular recusou a credencial deste computador.",
        ConnectionFailure.DeviceMismatch =>
            "o endereço pertence a outro dispositivo.",
        ConnectionFailure.ProtocolViolation =>
            "o celular enviou uma mensagem fora do protocolo. Verifique se os dois aplicativos estão atualizados.",
        ConnectionFailure.Closed =>
            "a conexão foi encerrada.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    public static string Describe(ThresholdPreset preset) => preset switch
    {
        ThresholdPreset.DarkEarlier => "Escuro mais cedo",
        ThresholdPreset.Balanced => "Equilibrado",
        ThresholdPreset.LightLonger => "Claro por mais tempo",
        ThresholdPreset.Personalized => "Personalizado",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    public static string Describe(ThemeKind theme) => theme == ThemeKind.Dark ? "escuro" : "claro";

    private static bool InSession(RuntimeState state) =>
        state is RuntimeState.WaitingForSensor or RuntimeState.Stabilizing or RuntimeState.Ready or RuntimeState.Active;

    private static string Network(CoordinatorStatus status, (Endpoint Endpoint, ConnectionFailure Failure)? lastFailure)
    {
        if (status.State == RuntimeState.Unpaired)
        {
            return "Nenhum celular pareado.";
        }

        if (InSession(status.State))
        {
            return "Conectado.";
        }

        var headline = status.State switch
        {
            RuntimeState.Discovering => "Procurando o celular…",
            RuntimeState.Connecting or RuntimeState.Reconnecting or RuntimeState.Authenticating => "Conectando ao celular…",
            RuntimeState.Degraded => "Sem sinal recente do celular.",
            _ => "Sem conexão com o celular.",
        };

        return lastFailure is { } failure
            ? $"{headline} Última tentativa em {failure.Endpoint}: {Describe(failure.Failure)}"
            : headline;
    }

    private static string Identity(CoordinatorStatus status, AppSettings settings,
        (Endpoint Endpoint, ConnectionFailure Failure)? lastFailure)
    {
        if (settings.PairedDevice is not { } device)
        {
            return "—";
        }

        if (status.Fatal is not null)
        {
            return "O celular recusou este computador. Pareie novamente.";
        }

        if (InSession(status.State))
        {
            return $"Identidade verificada: {device.Name}";
        }

        return lastFailure?.Failure is ConnectionFailure.TlsIdentityMismatch or ConnectionFailure.DeviceMismatch
            ? "O endereço respondeu com outra identidade. Confira o endereço; se o celular foi reinstalado, pareie novamente."
            : $"Pareado com {device.Name} (ainda não verificado nesta sessão).";
    }

    private static string Sensor(CoordinatorStatus status)
    {
        var lux = status.Lux is { } value
            ? value.ToString("0", CultureInfo.InvariantCulture) + " lx"
            : null;

        return (status.State, status.Sensor) switch
        {
            (RuntimeState.WaitingForSensor, SensorState.Unavailable) => "Sensor de luz ausente ou indisponível no celular.",
            (RuntimeState.WaitingForSensor, SensorState.Error) => "Erro no sensor de luz do celular.",
            (RuntimeState.WaitingForSensor, SensorState.Uncertain) =>
                "Leitura incerta (tela apagada ou sensor suspenso). Automação suspensa.",
            (RuntimeState.WaitingForSensor, _) => "Aguardando uma leitura nova do sensor.",
            (RuntimeState.Stabilizing, _) => $"Estabilizando… {lux}",
            (RuntimeState.Ready or RuntimeState.Active, _) => lux ?? "—",
            (RuntimeState.Degraded, _) => "Sem sinal recente; a última leitura não é mais confiável.",
            _ => "—",
        };
    }

    private static string Automation(CoordinatorStatus status)
    {
        if (status.Mode == DesiredMode.ManualPause)
        {
            return status.PauseReason == PauseReason.ExternalThemeChange
                ? "Pausado: tema modificado externamente"
                : "Pausado por você";
        }

        if (status.Mode == DesiredMode.RecommendOnly)
        {
            return "Somente recomendar";
        }

        if (status.ApplyBlocked)
        {
            return "Suspenso: falha ao aplicar o tema. Retome o automático para tentar de novo.";
        }

        if (status.ContrastBlocked)
        {
            return "Suspenso: tema de contraste ativo";
        }

        return status.State switch
        {
            RuntimeState.Active => "Automação ativa",
            RuntimeState.Unpaired => "Automático aguardando pareamento",
            _ => "Sensor indisponível: aguardando recuperação",
        };
    }

    private static string Recommendation(Recommendation recommendation)
    {
        if (recommendation.Target is not { } target)
        {
            return "Manter o tema atual";
        }

        if (!recommendation.Stable)
        {
            return $"Possível mudança para {Describe(target)} (aguardando estabilidade)";
        }

        return recommendation.InCooldown
            ? $"Recomendado: tema {Describe(target)} (aguardando intervalo mínimo entre trocas)"
            : $"Recomendado: tema {Describe(target)}";
    }
}
