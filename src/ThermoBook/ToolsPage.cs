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
    private readonly Func<CleanupSchedule?> _getSchedule;
    private readonly Action<CleanupSchedule?> _setSchedule;
    private readonly Func<CleanupLog?> _readLog;
    private bool _syncing;

    internal readonly ComboBox ScheduleKind, ScheduleDay;
    internal readonly NumericUpDown ScheduleHour, ScheduleMinute;
    internal readonly Button ScheduleApply;
    internal readonly Label ScheduleLast;

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
        Func<string, Task>? removeApp = null, Func<string>? defenderStatus = null, Func<string, bool>? confirm = null,
        Func<CleanupSchedule?>? getSchedule = null, Action<CleanupSchedule?>? setSchedule = null, Func<CleanupLog?>? readLog = null)
    {
        _getSchedule = getSchedule ?? (() => null);
        _setSchedule = setSchedule ?? (_ => { });
        _readLog = readLog ?? (() => ScheduledCleanup.ReadLog(ScheduledCleanup.LogPath));
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

        _list.Controls.Add(Theme.Header("Limpeza automática"));
        _list.Controls.Add(Theme.Note("Roda sozinha pelo Agendador de Tarefas, só na tomada, sem esvaziar a Lixeira. Ligar e desligar pedem administrador (uma vez). Guarda uma cópia do app em Arquivos de Programas."));
        ScheduleKind = Combo(130, "Desligada", "Toda semana", "Todo mês");
        ScheduleDay = Combo(130);
        ScheduleHour = Number(0, 23);
        ScheduleMinute = Number(0, 59);
        ScheduleKind.SelectedIndexChanged += (_, _) => { if (!_syncing) FillDays(); };
        ScheduleApply = Theme.Button("Salvar agendamento (administrador)");
        ScheduleApply.Click += async (_, _) => await ApplyScheduleAsync();
        _list.Controls.Add(Row(ScheduleKind, ScheduleDay, Caption("às"), ScheduleHour, Caption(":"), ScheduleMinute));
        ScheduleApply.Margin = new Padding(12, 6, 3, 3);
        _list.Controls.Add(ScheduleApply);
        ScheduleLast = new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Muted, Margin = new Padding(12, 2, 3, 6) };
        _list.Controls.Add(ScheduleLast);
        ShowSchedule();

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

    private static ComboBox Combo(int width, params string[] items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Theme.Raised, ForeColor = Theme.Fg, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 6, 4, 3) };
        box.Items.AddRange(items);
        return box;
    }

    private static NumericUpDown Number(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = 52, BackColor = Theme.Raised, ForeColor = Theme.Fg, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 3) };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Fg, Margin = new Padding(4, 9, 4, 0) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        row.Controls.AddRange(controls);
        return row;
    }

    /// <summary>Dias da lista conforme a frequência: dias da semana (semanal) ou 1–28 (mensal); vazia e desligada se "Desligada".</summary>
    private void FillDays()
    {
        int previous = ScheduleDay.SelectedIndex;
        ScheduleDay.Items.Clear();
        if (ScheduleKind.SelectedIndex == 1)
        {
            var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat;
            for (int d = 0; d < 7; d++) ScheduleDay.Items.Add(pt.GetDayName((DayOfWeek)d));
        }
        else if (ScheduleKind.SelectedIndex == 2)
            for (int d = 1; d <= 28; d++) ScheduleDay.Items.Add($"dia {d}");
        ScheduleDay.Enabled = ScheduleHour.Enabled = ScheduleMinute.Enabled = ScheduleKind.SelectedIndex > 0;
        if (ScheduleDay.Items.Count > 0) ScheduleDay.SelectedIndex = Math.Clamp(previous, 0, ScheduleDay.Items.Count - 1);
    }

    /// <summary>O que o usuário escolheu na tela, ou null se "Desligada".</summary>
    internal CleanupSchedule? ChosenSchedule() => ScheduleKind.SelectedIndex switch
    {
        1 => new(ScheduleFrequency.Weekly, ScheduleDay.SelectedIndex, (int)ScheduleHour.Value, (int)ScheduleMinute.Value),
        2 => new(ScheduleFrequency.Monthly, ScheduleDay.SelectedIndex + 1, (int)ScheduleHour.Value, (int)ScheduleMinute.Value),
        _ => null,
    };

    /// <summary>Mostra o que está salvo e a última limpeza automática; não dispara nada.</summary>
    internal void ShowSchedule()
    {
        _syncing = true;
        try
        {
            var s = _getSchedule();
            ScheduleKind.SelectedIndex = s is null ? 0 : s.Frequency == ScheduleFrequency.Weekly ? 1 : 2;
            FillDays();
            if (s is not null)
            {
                ScheduleDay.SelectedIndex = s.Frequency == ScheduleFrequency.Weekly ? s.Day : s.Day - 1;
                ScheduleHour.Value = s.Hour;
                ScheduleMinute.Value = s.Minute;
            }
            else { ScheduleHour.Value = CleanupSchedule.Default.Hour; ScheduleMinute.Value = CleanupSchedule.Default.Minute; }
        }
        finally { _syncing = false; }
        ShowLast();
    }

    internal void ShowLast()
    {
        var log = _readLog();
        ScheduleLast.Text = log is null
            ? "Última limpeza automática: ainda não rodou."
            : $"Última limpeza automática: {log.When:dd/MM/yyyy HH:mm}, " + (log.Outcome == TaskOutcome.Ok ? log.Message : $"falhou ({log.Message})");
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) ShowLast(); // o resultado muda sem o app: lê o arquivo de novo ao mostrar a aba
    }

    /// <summary>Liga, troca ou desliga a tarefa no Agendador (UAC). Só guarda o horário se deu certo.</summary>
    internal async Task ApplyScheduleAsync()
    {
        var chosen = ChosenSchedule();
        var current = _getSchedule();
        if (chosen is null && current is null) { _status.Text = "A limpeza automática já está desligada."; return; }
        SetBusy(true);
        var report = await _elevated(chosen is null ? "limpeza-desagendar" : ElevatedTask.SchedulePrefix + chosen.Format());
        if (report.Outcome == TaskOutcome.Ok) _setSchedule(chosen);
        _status.Text = report.Message;
        SetBusy(false);
        ShowSchedule(); // volta ao que ficou valendo (se cancelou ou falhou, o anterior)
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
        _clean.Enabled = _apply.Enabled = _undo.Enabled = ScheduleApply.Enabled = !busy; // um UAC por vez
        if (busy) _status.Text = "Trabalhando...";
    }
}
