using ThermoBook.Services;

namespace ThermoBook;

/// <summary>
/// Aba Configurações: pílula, atalho, alerta e iniciar com o Windows. Não guarda estado: cada mudança vai para o TrayApp,
/// que salva e devolve o resultado por <see cref="Show"/> (o mesmo caminho do menu da bandeja).
/// </summary>
internal sealed class SettingsPage : UserControl
{
    private readonly Func<Func<Settings, Settings>, string?> _change;
    private Settings _shown = new();
    private bool _syncing;

    internal readonly CheckBox Pill, Startup, AutoCool, AutoOn, History;
    internal readonly ComboBox OnBattery, OnAc, ProgramName, ProgramMode;
    internal readonly ListBox Programs;
    internal readonly Button ProgramAdd, ProgramRemove;
    internal readonly NumericUpDown AlertCelsius, AlertSeconds;
    internal readonly TextBox Hotkey;
    internal readonly Button HotkeyApply;
    internal readonly Label HotkeyStatus;

    /// <param name="change">Aplica uma mudança nas configurações; devolve a mensagem de erro, ou null.</param>
    /// <param name="setStartup">Liga/desliga a Run key (não fica no settings.json).</param>
    public SettingsPage(Func<Func<Settings, Settings>, string?> change, Action<bool> setStartup)
    {
        _change = change;
        SuspendLayout();
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;

        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };

        list.Controls.Add(Theme.Header("Pílula"));
        Pill = Check("Mostrar a pílula flutuante");
        Pill.CheckedChanged += (_, _) => Change(s => s with { ShowPill = Pill.Checked });
        list.Controls.Add(Pill);
        list.Controls.Add(Theme.Note("Some sozinha quando um app está em tela cheia. Arraste para mudar de borda."));

        list.Controls.Add(Theme.Header("Atalho para desligar a tela"));
        Hotkey = new TextBox { Width = 200, BackColor = Theme.Raised, ForeColor = Theme.Fg, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(12, 6, 4, 3) };
        HotkeyApply = Theme.Button("Usar este atalho");
        HotkeyApply.Click += (_, _) => ApplyHotkey();
        Hotkey.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ApplyHotkey(); e.SuppressKeyPress = true; } };
        HotkeyStatus = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(8, 10, 3, 0) };
        var hotkeyRow = Row(Hotkey, HotkeyApply, HotkeyStatus);
        list.Controls.Add(hotkeyRow);
        list.Controls.Add(Theme.Note("Ctrl, Alt, Shift ou Win + uma letra, número ou F1–F24. Exemplo: Ctrl+Alt+Shift+D. Vazio desliga o atalho."));

        list.Controls.Add(Theme.Header("Alerta de temperatura"));
        AlertCelsius = Number(60, 105);
        AlertSeconds = Number(5, 600);
        AlertCelsius.ValueChanged += (_, _) => Change(s => s with { AlertCelsius = (double)AlertCelsius.Value });
        AlertSeconds.ValueChanged += (_, _) => Change(s => s with { AlertSeconds = (int)AlertSeconds.Value });
        list.Controls.Add(Row(Caption("Avisar acima de"), AlertCelsius, Caption("°C por"), AlertSeconds, Caption("segundos seguidos")));
        AutoCool = Check("No aviso, trocar sozinho para o modo Frio");
        AutoCool.CheckedChanged += (_, _) => Change(s => s with { AutoCool = AutoCool.Checked });
        list.Controls.Add(AutoCool);

        list.Controls.Add(Theme.Header("Modo automático"));
        AutoOn = Check("Trocar de modo sozinho");
        AutoOn.CheckedChanged += (_, _) => ChangeRules(r => r with { Enabled = AutoOn.Checked });
        list.Controls.Add(AutoOn);
        OnBattery = ModeBox(true);
        OnAc = ModeBox(true);
        OnBattery.SelectedIndexChanged += (_, _) => ChangeRules(r => r with { OnBattery = ModeOf(OnBattery) });
        OnAc.SelectedIndexChanged += (_, _) => ChangeRules(r => r with { OnAc = ModeOf(OnAc) });
        list.Controls.Add(Row(Caption("Na bateria:"), OnBattery, Caption("Na tomada:"), OnAc));
        Programs = new ListBox { Width = 330, Height = 84, BackColor = Theme.Raised, ForeColor = Theme.Fg, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(12, 6, 3, 3) };
        Programs.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveProgram(); };
        list.Controls.Add(Programs);
        ProgramName = new ComboBox { Width = 150, BackColor = Theme.Raised, ForeColor = Theme.Fg, FlatStyle = FlatStyle.Flat, Margin = new Padding(12, 6, 4, 3) };
        ProgramName.DropDown += (_, _) => FillRunning();
        ProgramMode = ModeBox(false);
        ProgramMode.SelectedIndex = 1; // Frio
        ProgramAdd = Theme.Button("Adicionar");
        ProgramAdd.Click += (_, _) => AddProgram();
        ProgramRemove = Theme.Button("Remover");
        ProgramRemove.Click += (_, _) => RemoveProgram();
        list.Controls.Add(Row(ProgramName, Caption("→"), ProgramMode, ProgramAdd, ProgramRemove));
        list.Controls.Add(Theme.Note("Programa aberto vence tomada/bateria; o primeiro da lista vale. Escreva o nome do .exe ou abra a lista para escolher um processo aberto. Trocar o modo à mão vale até as regras pedirem outro."));

        list.Controls.Add(Theme.Header("Histórico"));
        History = Check("Gravar o histórico (um minuto por linha, 30 dias)");
        History.CheckedChanged += (_, _) => Change(s => s with { History = History.Checked });
        list.Controls.Add(History);
        list.Controls.Add(Theme.Note("Arquivos CSV em %AppData%\\ThermoBook\\historico, um por dia. Abrem direto no Excel."));

        list.Controls.Add(Theme.Header("Inicialização"));
        Startup = Check("Iniciar com o Windows");
        Startup.CheckedChanged += (_, _) => { if (!_syncing) setStartup(Startup.Checked); };
        list.Controls.Add(Startup);

        Controls.Add(list);
        ResumeLayout();
    }

    /// <summary>Mostra as configurações salvas sem disparar mudanças.</summary>
    public void Show(Settings s, bool startup)
    {
        _syncing = true;
        try
        {
            _shown = s;
            Pill.Checked = s.ShowPill;
            AutoCool.Checked = s.AutoCool;
            History.Checked = s.History;
            Startup.Checked = startup;
            var rules = s.Auto ?? AutoRules.Default;
            AutoOn.Checked = rules.Enabled;
            Select(OnBattery, rules.OnBattery);
            Select(OnAc, rules.OnAc);
            Programs.Items.Clear();
            foreach (var p in rules.Programs ?? []) Programs.Items.Add($"{p.Process} → {p.Mode}");
            AlertCelsius.Value = Math.Clamp((decimal)s.AlertCelsius, AlertCelsius.Minimum, AlertCelsius.Maximum);
            AlertSeconds.Value = Math.Clamp(s.AlertSeconds, AlertSeconds.Minimum, AlertSeconds.Maximum);
            if (!Hotkey.Focused) Hotkey.Text = s.Hotkey;
        }
        finally { _syncing = false; }
    }

    private void Change(Func<Settings, Settings> edit)
    {
        if (!_syncing) _change(edit);
    }

    private void ChangeRules(Func<AutoRules, AutoRules> edit) => Change(s => s with { Auto = edit(s.Auto ?? AutoRules.Default) });

    /// <summary>Caixa de modos; com <paramref name="none"/>, o primeiro item é "(nenhum)" (sem regra).</summary>
    private static ComboBox ModeBox(bool none)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, BackColor = Theme.Raised, ForeColor = Theme.Fg, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 6, 4, 3) };
        if (none) box.Items.Add("(nenhum)");
        foreach (var m in PowerService.Modes) box.Items.Add(m.Name);
        return box;
    }

    private static string? ModeOf(ComboBox box) => box.SelectedIndex <= 0 && box.Items[0] is "(nenhum)" ? null : (string?)box.SelectedItem;

    private static void Select(ComboBox box, string? mode) =>
        box.SelectedIndex = mode is null ? 0 : Math.Max(0, box.Items.IndexOf(mode));

    internal void AddProgram()
    {
        string name = ProgramName.Text.Trim();
        if (name.Length == 0 || ProgramMode.SelectedItem is not string mode) return;
        ChangeRules(r =>
        {
            var list = (r.Programs ?? []).Where(p => !SameProcess(p.Process, name)).ToList();
            list.Add(new(name, mode));
            return r with { Programs = list };
        });
        ProgramName.Text = "";
    }

    internal void RemoveProgram()
    {
        int i = Programs.SelectedIndex;
        if (i < 0) return;
        ChangeRules(r => r with { Programs = (r.Programs ?? []).Where((_, k) => k != i).ToList() });
        if (Programs.Items.Count > 0) Programs.SelectedIndex = Math.Min(i, Programs.Items.Count - 1);
    }

    private static bool SameProcess(string a, string b) =>
        string.Equals(a.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), b.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);

    /// <summary>Preenche a lista com os processos abertos (uma vez, ao abrir a lista; não é consulta de sensor).</summary>
    private void FillRunning()
    {
        var processes = System.Diagnostics.Process.GetProcesses();
        try
        {
            var names = processes.Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            ProgramName.Items.Clear();
            ProgramName.Items.AddRange(names);
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    internal void ApplyHotkey()
    {
        string text = Hotkey.Text.Trim();
        if (text.Length > 0 && !HotkeyService.TryParse(text, out _, out _))
        {
            HotkeyStatus.Text = "Atalho inválido.";
            return;
        }
        string? error = _change(s => s with { Hotkey = text });
        HotkeyStatus.Text = error ?? (text.Length == 0 ? "Atalho desligado." : "Atalho registrado.");
        Hotkey.Text = _shown.Hotkey; // o que ficou valendo (o anterior, se o novo falhou)
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(12, 2, 3, 2) };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Fg, Margin = new Padding(4, 9, 4, 0) };

    private static NumericUpDown Number(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = 64, BackColor = Theme.Raised, ForeColor = Theme.Fg, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 6, 4, 3) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        row.Controls.AddRange(controls);
        return row;
    }
}
