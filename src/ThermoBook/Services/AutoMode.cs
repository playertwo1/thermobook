namespace ThermoBook.Services;

/// <summary>Programa que, aberto, pede um modo. <paramref name="Process"/> é o nome do executável, com ou sem ".exe".</summary>
internal sealed record ProgramRule(string Process, string Mode);

/// <summary>Regras do modo automático (fase 13). Nome de modo nulo ou desconhecido = sem regra para aquela condição.</summary>
/// <param name="Programs">Em ordem de prioridade: vale o primeiro programa da lista que estiver aberto.</param>
internal sealed record AutoRules(bool Enabled = false, string? OnBattery = "Economia", string? OnAc = "Equilibrado",
    List<ProgramRule>? Programs = null)
{
    /// <summary>Regras de exemplo do backlog, mostradas quando o usuário ainda não configurou nada.</summary>
    public static AutoRules Default => new(Programs: [new("ffmpeg", "Frio")]);
}

/// <summary>
/// Decide o modo pelas regras. Prioridade: temperatura alta (Frio automático do alerta) > programa aberto > tomada/bateria.
/// Só pede troca quando o modo que as regras querem <b>muda</b>: assim uma troca manual (pelo app ou pelo Windows)
/// vale até as regras pedirem outro modo. Lógica pura; quem lê o sistema e aplica é o <c>TrayApp</c>.
/// </summary>
internal sealed class AutoMode
{
    private PowerMode? _last;
    private bool _active;

    /// <summary>O modo que as regras querem agora, ou null se nenhuma regra vale.</summary>
    public static PowerMode? Target(AutoRules rules, bool onAc, bool hot, IEnumerable<string> running)
    {
        if (hot) return PowerService.ModeByName("Frio");
        var names = running.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules.Programs ?? [])
            if (names.Contains(Normalize(rule.Process)) && PowerService.ModeByName(rule.Mode) is { } mode) return mode;
        return PowerService.ModeByName(onAc ? rules.OnAc : rules.OnBattery);
    }

    /// <returns>O modo a aplicar agora, ou null para não mexer.</returns>
    public PowerMode? Decide(AutoRules? rules, bool onAc, bool hot, IEnumerable<string> running)
    {
        if (rules is not { Enabled: true })
        {
            _active = false; // ao religar, aplica de novo o que as regras querem
            return null;
        }
        var target = Target(rules, onAc, hot, running);
        bool changed = !_active || target != _last;
        _active = true;
        _last = target;
        return changed ? target : null;
    }

    private static string Normalize(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
