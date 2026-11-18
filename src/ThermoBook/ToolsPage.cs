using Microsoft.Win32;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>
/// Aba Ferramentas do painel completo: limpeza (11.2) e otimização do Windows (11.3), com itens marcáveis agrupados como o .ps1,
/// Aplicar, Desfazer e o status do Defender no fim.
/// Ajustes de HKCU rodam aqui, sem administrador; limpeza, Delivery Optimization e TRIM vão para o processo elevado (11.1).
/// </summary>
internal sealed class ToolsPage : UserControl
{
    internal const string RemoveWarning = "Isso não pode ser desfeito pelo ThermoBook; reinstale pela Microsoft Store.";
    internal const string RecycleWarning = "Isto também esvazia a Lixeira de todas as unidades. Não tem volta. Continuar?";

    private readonly Dictionary<string, int?> _backup;
    private readonly Action<Dictionary<string, int?>> _persist;
    private readonly RegistryKey _userRoot, _machineRoot;
    private readonly Func<string, Task<TaskReport>> _elevated;
    private readonly Func<IReadOnlyList<BloatApp>> _listApps;
    private readonly Func<string, Task> _removeApp;
    private readonly Func<string> _defenderStatus;
    private readonly Func<string, bool> _confirm;

    internal readonly CheckBox Recycle;
    internal readonly Dictionary<Tweak, CheckBox> TweakBoxes = [];
    internal readonly CheckBox Trim;
    internal readonly List<(BloatApp App, CheckBox Box)> AppBoxes = [];
    private readonly FlowLayoutPanel _list;
    private readonly Label _appsNote, _status, _defender;
    private readonly Button _clean, _apply, _undo;

    /// <param name="root">Só nos testes: chave falsa usada no lugar de HKCU e de HKLM.</param>
    public ToolsPage(Dictionary<string, int?> backup, Action<Dictionary<string, int?>> persist, RegistryKey? root = null,
        Func<string, Task<TaskReport>>? elevated = null, Func<IReadOnlyList<BloatApp>>? listApps = null,
        Func<string, Task>? removeApp = null, Func<string>? defenderStatus = null, Func<string, bool>? confirm = null)
    {
        _backup = backup;
        _persist = persist;
        _userRoot = root ?? Registry.CurrentUser;
        _machineRoot = root ?? Registry.LocalMachine;
        _elevated = elevated ?? (task => ElevatedTask.LaunchAsync(task));
        _listApps = listApps ?? Tweaks.InstalledBloat;
        _removeApp = removeApp ?? Tweaks.RemoveApp;
        _defenderStatus = defenderStatus ?? Tweaks.DefenderStatus;
        _confirm = confirm ?? (text => MessageBox.Show(this, text, "ThermoBook", MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK);

        SuspendLayout();
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;

        _list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        _list.Controls.Add(Theme.Header("Limpeza"));
        _list.Controls.Add(Theme.Note("Temporários, relatórios de erro, cache do Windows Update e cache DNS. Itens em uso são pulados. Pede permissão de administrador."));
        Recycle = Check("Também esvaziar a Lixeira (não tem volta)", @checked: false);
        _list.Controls.Add(Recycle);
        _clean = Theme.Button("Limpar agora (administrador)");
        _clean.Margin = new Padding(12, 6, 3, 6);
        _clean.Click += async (_, _) => await CleanAsync();
        _list.Controls.Add(_clean);

        foreach (var group in Tweaks.All.GroupBy(t => t.Group))
        {
            _list.Controls.Add(Theme.Header(group.Key));
            foreach (var t in group)
            {
                var box = Check(t.Label, @checked: true);
                TweakBoxes[t] = box;
                _list.Controls.Add(box);
            }
        }
        _list.Controls.Add(Theme.Header("Disco"));
        Trim = Check("TRIM nos SSDs (administrador)", @checked: true);
        _list.Controls.Add(Trim);
        _list.Controls.Add(Theme.Header("Apps pré-instalados"));
        _appsNote = Theme.Note("Procurando apps...");
        _list.Controls.Add(_appsNote);

        _apply = Theme.Button("Aplicar ajustes");
        _undo = Theme.Button("Desfazer ajustes");
        _apply.Click += async (_, _) => await ApplyAsync();
        _undo.Click += async (_, _) => await UndoAsync();
        _status = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(3, 8, 3, 0) };
        _defender = new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Muted, Margin = new Padding(3, 8, 3, 0) };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 6, 12, 12) };
        bottom.Controls.AddRange([_apply, _undo]);
        bottom.SetFlowBreak(_undo, true);
        bottom.Controls.AddRange([_status, _defender]);
        bottom.SetFlowBreak(_status, true);

        Controls.Add(_list);
        Controls.Add(bottom);
        RefreshApplied();
        ResumeLayout();
    }

    private static CheckBox Check(string text, bool @checked) =>
        new() { Text = text, AutoSize = true, Checked = @checked, Margin = new Padding(12, 2, 3, 2) };

    // Roda só quando a aba aparece pela primeira vez: abrir o painel não lista apps nem consulta o Defender.
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _defender.Text = "Defender: lendo...";
        var defender = Task.Run(_defenderStatus); // WMI e PackageManager nunca na thread da interface
        IReadOnlyList<BloatApp>? apps = null;
        string? error = null;
        try { apps = await Task.Run(_listApps); }
        catch (Exception ex) { error = ex.Message; }
        string status = await defender;
        if (IsDisposed) return; // fechou antes de terminar
        if (apps is not null) ShowApps(apps);
        else _appsNote.Text = $"Não consegui listar os apps: {error}";
        _defender.Text = status;
    }

    /// <summary>Mostra os apps encontrados, todos desmarcados.</summary>
    internal void ShowApps(IReadOnlyList<BloatApp> apps)
    {
        _list.SuspendLayout();
        foreach (var (_, box) in AppBoxes) { _list.Controls.Remove(box); box.Dispose(); }
        AppBoxes.Clear();
        foreach (var app in apps)
        {
            var box = Check(app.DisplayName, @checked: false);
            AppBoxes.Add((app, box));
            _list.Controls.Add(box);
        }
        _appsNote.Text = apps.Count == 0 ? "Nenhum dos apps da lista está instalado." : $"Marque só o que quiser remover. {RemoveWarning}";
        _list.ResumeLayout();
    }

    /// <summary>Marca com "(aplicado)" os ajustes que estão no backup.</summary>
    private void RefreshApplied()
    {
        var machine = Tweaks.LoadMachineBackup(_machineRoot);
        foreach (var (t, box) in TweakBoxes)
            box.Text = (t.Machine ? machine : _backup).ContainsKey(t.Key) ? $"{t.Label} (aplicado)" : t.Label;
    }

    private void Persist() => _persist(new(_backup));

    /// <summary>Limpeza no processo elevado; a Lixeira só com a caixa marcada e depois de confirmar.</summary>
    internal async Task CleanAsync()
    {
        bool recycle = Recycle.Checked;
        if (recycle && !_confirm(RecycleWarning)) return;
        SetBusy(true);
        _status.Text = (await _elevated(recycle ? "limpeza-lixeira" : "limpeza")).Message;
        SetBusy(false);
    }

    internal async Task ApplyAsync()
    {
        var apps = AppBoxes.Where(a => a.Box.Checked).Select(a => a.App).ToList();
        if (apps.Count > 0 && !_confirm($"Remover {apps.Count} app(s): {string.Join(", ", apps.Select(a => a.DisplayName))}?\n\n{RemoveWarning}"))
            return;

        SetBusy(true);
        var lines = new List<string>();
        int applied = 0;
        foreach (var (t, box) in TweakBoxes)
        {
            if (t.Machine || !box.Checked) continue;
            if (Tweaks.Apply(_userRoot, t, _backup, Persist)) applied++;
            else lines.Add($"Não aplicado: {t.Label}.");
        }
        if (applied > 0) lines.Insert(0, $"{applied} ajuste(s) aplicado(s). Alguns só aparecem depois de sair e entrar na conta.");

        bool delivery = TweakBoxes.Any(x => x.Key.Machine && x.Value.Checked);
        if (delivery || Trim.Checked)
            lines.Add((await _elevated("otimizacao" + (delivery ? "-entrega" : "") + (Trim.Checked ? "-trim" : ""))).Message);

        foreach (var app in apps)
        {
            try
            {
                await _removeApp(app.FullName);
                lines.Add($"Removido: {app.DisplayName}.");
                var entry = AppBoxes.First(a => a.App == app);
                AppBoxes.Remove(entry);
                _list.Controls.Remove(entry.Box);
                entry.Box.Dispose();
            }
            catch (Exception ex) { lines.Add($"Não removido: {app.DisplayName} ({ex.Message})."); }
        }

        _status.Text = lines.Count == 0 ? "Nada marcado." : string.Join(Environment.NewLine, lines);
        RefreshApplied();
        SetBusy(false);
    }

    internal async Task UndoAsync()
    {
        SetBusy(true);
        int undone = Tweaks.Undo(_userRoot, Tweaks.All.Where(t => !t.Machine), _backup, Persist);
        var lines = new List<string> { undone > 0 ? $"{undone} ajuste(s) desfeito(s)." : "Nenhum ajuste da conta para desfazer." };
        if (Tweaks.LoadMachineBackup(_machineRoot).Count > 0) lines.Add((await _elevated("otimizacao-desfazer")).Message);
        _status.Text = string.Join(Environment.NewLine, lines);
        RefreshApplied();
        SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        _clean.Enabled = _apply.Enabled = _undo.Enabled = !busy; // um UAC por vez
        if (busy) _status.Text = "Trabalhando...";
    }
}
