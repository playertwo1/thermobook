namespace ThermoBook.Services;

/// <summary>Valores em tomada (AC) e bateria (DC).</summary>
internal sealed record PairValue(uint Ac, uint Dc);

/// <summary>
/// Modo segundo plano: tampa fechada e inatividade não suspendem o PC. Guarda o que havia antes (por plano)
/// e devolve exatamente isso ao desligar. A lógica é pura: leitura, gravação e salvamento vêm de fora.
/// </summary>
internal static class BackgroundMode
{
    public delegate (uint Ac, uint Dc)? ReadFn(Guid plan, Guid sub, Guid setting);
    public delegate bool WriteFn(Guid plan, Guid sub, Guid setting, uint ac, uint dc);

    public static readonly (string Key, Guid Sub, Guid Setting)[] Items =
    [
        ("lid", PowerService.SubButtons, PowerService.LidAction),
        ("standby", PowerService.SubSleep, PowerService.StandbyIdle),
        ("hibernate", PowerService.SubSleep, PowerService.HibernateIdle),
    ];

    public static bool IsOn(Settings s) => s.BackgroundPlans is { Count: > 0 };

    /// <summary>Liga no plano. O snapshot é salvo ANTES de gravar e nunca é sobrescrito se o plano já tem um.</summary>
    public static Settings Enable(Settings s, Guid plan, ReadFn read, WriteFn write, Action<Settings> save)
    {
        var plans = new Dictionary<string, Dictionary<string, PairValue>>(s.BackgroundPlans ?? []);
        string key = plan.ToString();
        if (!plans.ContainsKey(key))
        {
            var snap = new Dictionary<string, PairValue>();
            foreach (var (k, sub, setting) in Items)
                if (read(plan, sub, setting) is { } v) snap[k] = new PairValue(v.Ac, v.Dc);
            plans[key] = snap;
            s = s with { BackgroundPlans = plans };
            save(s);
        }
        foreach (var (_, sub, setting) in Items) write(plan, sub, setting, 0, 0); // 0 = não fazer nada / nunca
        return s;
    }

    /// <summary>Desliga: restaura os valores guardados de todos os planos e só então apaga o snapshot (se a gravação falhar, ele continua salvo).</summary>
    public static Settings Disable(Settings s, WriteFn write, Action<Settings> save)
    {
        foreach (var (key, snap) in s.BackgroundPlans ?? [])
        {
            if (!Guid.TryParse(key, out var plan)) continue;
            foreach (var (k, sub, setting) in Items)
                if (snap.TryGetValue(k, out var v)) write(plan, sub, setting, v.Ac, v.Dc);
        }
        s = s with { BackgroundPlans = null };
        save(s);
        return s;
    }
}
