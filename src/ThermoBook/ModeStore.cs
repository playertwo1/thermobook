using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Estado de energia que a interface mostra: modo atual (null = Personalizado), turbo/limite e segundo plano.</summary>
/// <param name="Auto">O modo atual é o que o modo automático quer (fase 13).</param>
internal readonly record struct ModeState(PowerMode? Mode, uint Turbo, uint Limit, bool Background, bool Auto = false);

/// <summary>
/// Único lugar do estado do modo. Só o <c>TrayApp</c> lê o sistema e grava aqui; bandeja, pílula, painel rápido e painel completo
/// apenas assinam <see cref="Changed"/>. O evento só dispara quando algo mudou.
/// </summary>
internal sealed class ModeStore
{
    public ModeState? State { get; private set; }

    public event Action<ModeState>? Changed;

    /// <returns>true se o estado mudou (e o evento foi disparado).</returns>
    public bool Set(ModeState state)
    {
        if (State == state) return false;
        State = state;
        Changed?.Invoke(state);
        return true;
    }
}
