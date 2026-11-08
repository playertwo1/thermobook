namespace ThermoBook.Services;

/// <summary>
/// Avisa uma vez quando a temperatura fica acima do limite por um tempo seguido.
/// Só rearma depois de esfriar 5 °C abaixo do limite, para não repetir o aviso a cada oscilação.
/// </summary>
internal sealed class TempAlert(double limit, TimeSpan duration)
{
    private DateTime? _since;
    private bool _fired;

    /// <returns>true no momento em que o aviso deve ser mostrado.</returns>
    public bool Check(double celsius, DateTime now)
    {
        if (celsius <= limit)
        {
            _since = null;
            if (celsius < limit - 5) _fired = false;
            return false;
        }
        _since ??= now;
        if (_fired || now - _since < duration) return false;
        return _fired = true;
    }
}
