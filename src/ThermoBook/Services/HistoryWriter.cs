using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThermoBook.Services;

/// <summary>Uma leitura para o histórico. Valor ausente = NaN (vira célula vazia).</summary>
internal readonly record struct HistorySample(double Temp, double Cpu, double Gpu, double GpuTemp, double Ram, string Mode)
{
    public static HistorySample From(Snapshot s, string mode) => new(
        s.Temps.Length > 0 ? s.TempMax : double.NaN, s.CpuUse, s.Gpu, s.GpuTemp ?? double.NaN,
        s.RamTotal > 0 ? s.RamPercent : double.NaN, mode);
}

/// <summary>Uma linha do histórico lida do CSV. Célula vazia = NaN.</summary>
internal readonly record struct HistoryPoint(DateTime Time, double Temp, double Cpu, double Gpu, double Ram);

/// <summary>Lê os CSVs do <see cref="HistoryWriter"/> para o gráfico de 24 h. Linha quebrada, arquivo ausente ou em uso são pulados.</summary>
internal static class HistoryReader
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Pontos das 24 h até <paramref name="now"/>, em ordem de hora. Pode atravessar dois arquivos.</summary>
    public static List<HistoryPoint> Last24h(string dir, DateTime now)
    {
        var from = now.AddHours(-24);
        var points = new List<HistoryPoint>();
        for (var day = from.Date; day <= now.Date; day = day.AddDays(1))
        {
            string path = Path.Combine(dir, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
            foreach (var line in ReadLines(path))
                if (Parse(line) is { } p && p.Time >= from && p.Time <= now) points.Add(p);
        }
        points.Sort((a, b) => a.Time.CompareTo(b.Time));
        return points;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(file, Encoding.UTF8);
            var lines = new List<string>();
            while (r.ReadLine() is { } l) lines.Add(l);
            return lines;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    internal static HistoryPoint? Parse(string line)
    {
        var c = line.Split(';');
        if (c.Length != 7 || !DateTime.TryParseExact(c[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        double N(string s) => double.TryParse(s, NumberStyles.Float, Pt, out var v) ? v : double.NaN;
        return new(t, N(c[1]), N(c[2]), N(c[3]), N(c[5]));
    }

    /// <summary>Parte a série onde há buraco (app fechado): linha só liga pontos a até <paramref name="gapMinutes"/> minutos um do outro. NaN sai.</summary>
    public static List<List<(DateTime Time, double Value)>> Segments(IEnumerable<(DateTime Time, double Value)> series, int gapMinutes = 3)
    {
        var result = new List<List<(DateTime, double)>>();
        DateTime? last = null;
        foreach (var (time, value) in series)
        {
            if (!double.IsFinite(value)) continue;
            if (last is null || (time - last.Value).TotalMinutes > gapMinutes) result.Add([]);
            result[^1].Add((time, value));
            last = time;
        }
        return result;
    }
}

/// <summary>
/// Histórico (fase 16): junta as leituras de cada minuto e grava uma linha em <c>AAAA-MM-DD.csv</c> (um arquivo por dia).
/// Formato para o Excel em português: <c>;</c> entre colunas e vírgula decimal. Temperatura = máxima do minuto; o resto, a média.
/// Não consulta sensor: só recebe leituras. Quem chama decide a thread (a gravação em disco acontece em <see cref="Add"/> e <see cref="Flush"/>).
/// </summary>
internal sealed class HistoryWriter(string dir)
{
    internal const string Header = "hora;temperatura_c;cpu_pct;gpu_pct;gpu_temperatura_c;ram_pct;modo";
    internal const int KeepDays = 30;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex FileName = new(@"^(\d{4}-\d{2}-\d{2})\.csv$", RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly List<(DateTime Minute, string Line)> _pending = []; // linhas que não foram para o disco (ex.: arquivo aberto no Excel)
    private DateTime? _minute;
    private double _tempMax = double.NaN;
    private readonly double[] _sum = new double[4]; // cpu, gpu, gpu°C, ram
    private readonly int[] _count = new int[4];
    private string _mode = "";

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThermoBook", "historico");

    internal string PathFor(DateTime day) => Path.Combine(dir, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");

    /// <summary>Soma a leitura ao minuto de <paramref name="now"/>; ao entrar num minuto novo, grava o anterior.</summary>
    public void Add(DateTime now, HistorySample sample)
    {
        lock (_gate)
        {
            var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (_minute is { } m && minute != m) { Close(); }
            _minute = minute;
            if (double.IsFinite(sample.Temp) && (double.IsNaN(_tempMax) || sample.Temp > _tempMax)) _tempMax = sample.Temp;
            double[] v = [sample.Cpu, sample.Gpu, sample.GpuTemp, sample.Ram];
            for (int i = 0; i < v.Length; i++)
                if (double.IsFinite(v[i])) { _sum[i] += v[i]; _count[i]++; }
            _mode = sample.Mode;
            WritePending();
        }
    }

    /// <summary>Fecha o minuto em andamento (ao sair do app) e tenta gravar o que sobrou.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_minute is not null) Close();
            WritePending();
        }
    }

    private void Close()
    {
        string Avg(int i) => _count[i] == 0 ? "" : (_sum[i] / _count[i]).ToString("0.0", Pt);
        string temp = double.IsNaN(_tempMax) ? "" : _tempMax.ToString("0.0", Pt);
        string mode = _mode.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        var minute = _minute!.Value;
        _pending.Add((minute, $"{minute:yyyy-MM-dd HH:mm};{temp};{Avg(0)};{Avg(1)};{Avg(2)};{Avg(3)};{mode}"));
        _minute = null;
        _tempMax = double.NaN;
        Array.Clear(_sum);
        Array.Clear(_count);
        if (_pending.Count > 1440) _pending.RemoveRange(0, _pending.Count - 1440); // no máximo um dia de espera
    }

    /// <summary>Grava as linhas guardadas, cada uma no arquivo do seu dia. O que falhar fica para a próxima tentativa.</summary>
    private void WritePending()
    {
        if (_pending.Count == 0) return;
        var failed = new List<(DateTime, string)>();
        foreach (var day in _pending.GroupBy(p => p.Minute.Date))
        {
            try
            {
                Directory.CreateDirectory(dir);
                string path = PathFor(day.Key);
                bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
                // UTF-8 com BOM: o Excel reconhece os acentos do nome do modo.
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var w = new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                if (isNew) w.WriteLine(Header);
                foreach (var (_, line) in day) w.WriteLine(line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.AddRange(day); }
        }
        _pending.Clear();
        _pending.AddRange(failed);
    }

    /// <summary>Apaga os arquivos com mais de <see cref="KeepDays"/> dias (pelo nome da data). Só mexe em <c>AAAA-MM-DD.csv</c>.</summary>
    public void Prune(DateTime today)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                var m = FileName.Match(Path.GetFileName(path));
                if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
                if (today.Date - day > TimeSpan.FromDays(KeepDays))
                    try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
