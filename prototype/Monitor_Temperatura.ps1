Add-Type -AssemblyName System.Windows.Forms, System.Drawing
trap {
    [System.Windows.Forms.MessageBox]::Show("Erro inesperado:`n`n$_`n`n$($_.InvocationInfo.PositionMessage)", 'Monitor de Temperatura', 'OK', 'Error') | Out-Null
    exit 1
}

Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][Dpi]::SetProcessDPIAware()
[System.Windows.Forms.Application]::EnableVisualStyles()

$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, 'Local\MonitorTemperatura_Painel', [ref]$createdNew)
if (-not $createdNew) { exit 0 }

$dpiK = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero).DpiX / 96.0
function Px([double]$n) { [int][math]::Round($n * $dpiK) }

$SamsungSettingsApp = 'shell:AppsFolder\SAMSUNGELECTRONICSCO.LTD.SamsungSettings1.5_3c1yjt4zspk6g!App'

# ---------------------------------------------------------------------------
# Coleta em segundo plano: as consultas de GPU/disco levam ~1 s e travariam a janela.
# ---------------------------------------------------------------------------
$sync = [hashtable]::Synchronized(@{ Stop = $false; Data = $null; Seq = 0; Err = '' })
$collector = {
    while (-not $sync.Stop) {
        try {
            $cpu = Get-CimInstance Win32_PerfFormattedData_Counters_ProcessorInformation -Filter "Name='_Total'"

            # GPU como no Gerenciador de Tarefas: soma por motor, maximo por tipo de motor.
            $eng = @{}
            foreach ($g in Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine) {
                if ($g.Name -match '(luid_.+_eng_\d+)_engtype_(.*)$') {
                    $k = $matches[1]
                    if (-not $eng.ContainsKey($k)) { $eng[$k] = @{ T = $matches[2]; V = 0 } }
                    $eng[$k].V += $g.UtilizationPercentage
                }
            }
            $byType = @{}
            foreach ($e in $eng.Values) { if (-not $byType.ContainsKey($e.T) -or $e.V -gt $byType[$e.T]) { $byType[$e.T] = $e.V } }
            $gpuAll = 0; foreach ($v in $byType.Values) { if ($v -gt $gpuAll) { $gpuAll = $v } }
            $video = 0; foreach ($t in 'VideoDecode', 'VideoEncode', 'VideoProcessing', 'Video Codec') { if ($byType[$t] -gt $video) { $video = $byType[$t] } }
            $gpuMem = 0
            foreach ($m in Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory) {
                $u = [double]$m.DedicatedUsage + [double]$m.SharedUsage
                if ($u -gt $gpuMem) { $gpuMem = $u }
            }

            $os = Get-CimInstance Win32_OperatingSystem -Property TotalVisibleMemorySize, FreePhysicalMemory
            $disk = Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -Filter "Name='_Total'"
            $c = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
            $temps = @(Get-CimInstance Win32_PerfFormattedData_Counters_ThermalZoneInformation |
                       ForEach-Object { [math]::Round(($_.HighPrecisionTemperature - 2732) / 10.0, 0) })

            $sync.Data = [pscustomobject]@{
                CpuUse  = [math]::Min(100, [double]$cpu.PercentProcessorUtility)
                CpuGHz  = [double]$cpu.ProcessorFrequency * [double]$cpu.PercentProcessorPerformance / 100000.0
                CpuPerf = [double]$cpu.PercentProcessorPerformance
                Gpu     = [math]::Min(100, $gpuAll)
                Gpu3D   = [math]::Min(100, [double]$byType['3D'])
                GpuVid  = [math]::Min(100, $video)
                GpuMem  = $gpuMem
                RamTot  = [double]$os.TotalVisibleMemorySize * 1KB
                RamUsed = ([double]$os.TotalVisibleMemorySize - [double]$os.FreePhysicalMemory) * 1KB
                DiskAct = [math]::Max(0, [math]::Min(100, 100 - [double]$disk.PercentIdleTime))
                DiskRd  = [double]$disk.DiskReadBytesPersec
                DiskWr  = [double]$disk.DiskWriteBytesPersec
                CFree   = [double]$c.FreeSpace
                CSize   = [double]$c.Size
                Temps   = $temps
            }
            $sync.Seq++
            $sync.Err = ''
        } catch {
            $sync.Err = "$_"
        }
        Start-Sleep -Milliseconds 600
    }
}
$rs = [runspacefactory]::CreateRunspace()
$rs.Open()
$rs.SessionStateProxy.SetVariable('sync', $sync)
$ps = [powershell]::Create()
$ps.Runspace = $rs
[void]$ps.AddScript($collector)
$psHandle = $ps.BeginInvoke()

# ---------------------------------------------------------------------------
# Energia (powercfg no plano ativo, tomada e bateria)
# ---------------------------------------------------------------------------
$TurboNames = @{ 0 = 'Desligado'; 1 = 'Ligado'; 2 = 'Agressivo'; 3 = 'Eficiencia (padrao)'; 4 = 'Eficiencia agressiva' }

function Get-PowerValue([string]$alias) {
    # As duas ultimas linhas "...: 0xNN" sao os valores atuais (tomada, bateria); funciona em qualquer idioma.
    $vals = @(powercfg /qh SCHEME_CURRENT SUB_PROCESSOR $alias | Select-String ':\s*0x([0-9a-fA-F]+)\s*$' |
              ForEach-Object { [Convert]::ToInt32($_.Matches[0].Groups[1].Value, 16) })
    if ($vals.Count -ge 2) { return $vals[-2] }
    return $null
}

function Set-PowerValue([string]$alias, [int]$value) {
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR $alias $value | Out-Null
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR $alias $value | Out-Null
}

function Apply-Power([int]$turbo, [int]$limit) {
    Set-PowerValue PERFBOOSTMODE $turbo
    Set-PowerValue PROCTHROTTLEMAX $limit
    powercfg /setactive SCHEME_CURRENT | Out-Null
    Read-Power
}

# ---------------------------------------------------------------------------
# Interface (tema escuro)
# ---------------------------------------------------------------------------
$cBg     = [System.Drawing.Color]::FromArgb(24, 24, 27)
$cCard   = [System.Drawing.Color]::FromArgb(36, 36, 40)
$cText   = [System.Drawing.Color]::FromArgb(236, 236, 240)
$cDim    = [System.Drawing.Color]::FromArgb(160, 160, 170)
$cBlue   = [System.Drawing.Color]::FromArgb(64, 156, 255)
$cGreen  = [System.Drawing.Color]::FromArgb(80, 200, 120)
$cPurple = [System.Drawing.Color]::FromArgb(180, 130, 255)
$cTeal   = [System.Drawing.Color]::FromArgb(60, 200, 200)
$cOrange = [System.Drawing.Color]::FromArgb(255, 160, 60)
$cRed    = [System.Drawing.Color]::FromArgb(255, 85, 85)
$cYellow = [System.Drawing.Color]::FromArgb(240, 200, 60)

$fontUI    = New-Object System.Drawing.Font('Segoe UI', 9.5)
$fontTitle = New-Object System.Drawing.Font('Segoe UI Semibold', 10)
$fontValue = New-Object System.Drawing.Font('Segoe UI Semibold', 22)

$form = New-Object System.Windows.Forms.Form
$form.AutoScaleMode = 'None'
$form.Text = 'Monitor de Temperatura - Galaxy Book6 Pro'
$form.Font = $fontUI
$form.BackColor = $cBg
$form.ForeColor = $cText
$form.Size = New-Object System.Drawing.Size((Px 1060), (Px 720))
$form.MinimumSize = New-Object System.Drawing.Size((Px 820), (Px 600))
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Size = New-Object System.Drawing.Size([math]::Min($form.Width, $wa.Width - 20), [math]::Min($form.Height, $wa.Height - 20))
$form.StartPosition = 'CenterScreen'

function Set-DoubleBuffered($ctrl) {
    $ctrl.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($ctrl, $true, $null)
}

$cards = @{}
function New-Card([string]$key, [string]$title, $color, [double]$min, [double]$max) {
    $p = New-Object System.Windows.Forms.Panel
    $p.Dock = 'Fill'
    $p.BackColor = $cCard
    $p.Margin = New-Object System.Windows.Forms.Padding((Px 6))
    $p.Padding = New-Object System.Windows.Forms.Padding((Px 12), (Px 8), (Px 12), (Px 10))

    $graph = New-Object System.Windows.Forms.Panel
    $graph.Dock = 'Fill'
    $graph.BackColor = $cCard
    Set-DoubleBuffered $graph

    $detail = New-Object System.Windows.Forms.Label
    $detail.Dock = 'Top'; $detail.Height = (Px 22); $detail.ForeColor = $cDim; $detail.Text = '...'

    $value = New-Object System.Windows.Forms.Label
    $value.Dock = 'Top'; $value.Height = (Px 46); $value.Font = $fontValue; $value.ForeColor = $color; $value.Text = '--'

    $t = New-Object System.Windows.Forms.Label
    $t.Dock = 'Top'; $t.Height = (Px 22); $t.Font = $fontTitle; $t.ForeColor = $cDim; $t.Text = $title.ToUpper()

    # Fill primeiro, depois os Top de baixo para cima.
    $p.Controls.AddRange(@($graph, $detail, $value, $t))

    $card = @{ Value = $value; Detail = $detail; Graph = $graph; Color = $color; Min = $min; Max = $max
               Hist = New-Object System.Collections.Generic.List[double] }
    $graph.Tag = $card
    $graph.Add_Paint({ param($s, $e) Draw-Graph $s $e })
    $cards[$key] = $card
    return $p
}

function Draw-Graph($s, $e) {
    $c = $s.Tag
    $g = $e.Graphics
    $g.SmoothingMode = 'AntiAlias'
    $w = [double]$s.ClientSize.Width; $h = [double]$s.ClientSize.Height
    if ($w -lt 10 -or $h -lt 10) { return }
    $gridPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(55, 55, 62), 1)
    foreach ($f in 0.25, 0.5, 0.75) { $g.DrawLine($gridPen, 0, [float]($h * $f), [float]$w, [float]($h * $f)) }
    $gridPen.Dispose()
    $n = $c.Hist.Count
    if ($n -lt 2) { return }
    $step = $w / 59.0
    $pts = New-Object 'System.Drawing.PointF[]' ($n + 2)
    for ($i = 0; $i -lt $n; $i++) {
        $v = ($c.Hist[$i] - $c.Min) / ($c.Max - $c.Min)
        if ($v -lt 0) { $v = 0 }; if ($v -gt 1) { $v = 1 }
        $pts[$i] = New-Object System.Drawing.PointF([float]($w - ($n - 1 - $i) * $step), [float]($h - 1 - $v * ($h - 3)))
    }
    $pts[$n] = New-Object System.Drawing.PointF($pts[$n - 1].X, [float]$h)
    $pts[$n + 1] = New-Object System.Drawing.PointF($pts[0].X, [float]$h)
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(50, $c.Color))
    $g.FillPolygon($fill, $pts)
    $fill.Dispose()
    $pen = New-Object System.Drawing.Pen($c.Color, [float](Px 2))
    $g.DrawLines($pen, [System.Drawing.PointF[]]$pts[0..($n - 1)])
    $pen.Dispose()
}

function Push-Card([string]$key, [double]$v, [string]$valueText, [string]$detailText, $color = $null) {
    $c = $cards[$key]
    $c.Hist.Add($v)
    while ($c.Hist.Count -gt 60) { $c.Hist.RemoveAt(0) }
    $c.Value.Text = $valueText
    $c.Detail.Text = $detailText
    if ($color) { $c.Value.ForeColor = $color; $c.Color = $color }
    $c.Graph.Invalidate()
}

# --- Grade de cartoes ---
$grid = New-Object System.Windows.Forms.TableLayoutPanel
$grid.Dock = 'Fill'
$grid.Padding = New-Object System.Windows.Forms.Padding((Px 6))
$grid.ColumnCount = 3; $grid.RowCount = 2
1..3 | ForEach-Object { [void]$grid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('Percent', 33.33))) }
1..2 | ForEach-Object { [void]$grid.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Percent', 50))) }
$grid.Controls.Add((New-Card 'temp' 'Temperatura' $cGreen 30 100), 0, 0)
$grid.Controls.Add((New-Card 'cpu'  'Processador' $cBlue 0 100), 1, 0)
$grid.Controls.Add((New-Card 'gpu'  'GPU Intel Arc' $cGreen 0 100), 2, 0)
$grid.Controls.Add((New-Card 'ram'  'Memoria RAM' $cPurple 0 100), 0, 1)
$grid.Controls.Add((New-Card 'disk' 'Disco (SSD)' $cTeal 0 100), 1, 1)
$grid.Controls.Add((New-Card 'bat'  'Bateria' $cYellow 0 100), 2, 1)
$form.Controls.Add($grid)

# --- Painel de controle (topo) ---
function New-Btn([string]$text, [int]$w, $color) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text; $b.Width = (Px $w); $b.Height = (Px 34)
    $b.FlatStyle = 'Flat'
    $b.FlatAppearance.BorderColor = $color
    $b.BackColor = $cCard; $b.ForeColor = $cText
    $b.Margin = New-Object System.Windows.Forms.Padding((Px 4), (Px 3), (Px 4), (Px 3))
    return $b
}
function New-Lbl([string]$text, $color = $null) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text; $l.AutoSize = $true
    $l.ForeColor = $(if ($color) { $color } else { $cDim })
    $l.Margin = New-Object System.Windows.Forms.Padding((Px 4), (Px 11), (Px 4), 0)
    return $l
}

$top = New-Object System.Windows.Forms.FlowLayoutPanel
$top.Dock = 'Top'
$top.Height = (Px 104)
$top.Padding = New-Object System.Windows.Forms.Padding((Px 10), (Px 6), (Px 10), 0)
$top.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 34)

$tip = New-Object System.Windows.Forms.ToolTip
$btnCold   = New-Btn 'Frio' 100 $cBlue
$btnSilent = New-Btn 'Silencioso' 110 $cPurple
$btnNormal = New-Btn 'Normal' 100 $cGreen
$btnMax    = New-Btn 'Maximo' 100 $cRed
$btnSamsung = New-Btn 'Ventoinha (Samsung)...' 190 $cOrange
$tip.SetToolTip($btnCold, 'Turbo desligado, CPU ate 100% da frequencia base. Bem mais frio.')
$tip.SetToolTip($btnSilent, 'Turbo desligado e CPU limitada a 80%. O mais frio e silencioso.')
$tip.SetToolTip($btnNormal, 'Padrao de fabrica: turbo em modo eficiencia.')
$tip.SetToolTip($btnMax, 'Turbo agressivo: mais desempenho, mais calor.')
$tip.SetToolTip($btnSamsung, 'Abre Configuracoes Samsung > Bateria e desempenho. "Alto desempenho" faz a ventoinha girar mais forte.')

$cmbTurbo = New-Object System.Windows.Forms.ComboBox
$cmbTurbo.DropDownStyle = 'DropDownList'
$cmbTurbo.Width = (Px 190)
$cmbTurbo.Margin = New-Object System.Windows.Forms.Padding((Px 4), (Px 8), (Px 14), 0)
$TurboOrder = @(0, 3, 1, 2)
foreach ($v in $TurboOrder) { [void]$cmbTurbo.Items.Add($TurboNames[$v]) }

$track = New-Object System.Windows.Forms.TrackBar
$track.Minimum = 50; $track.Maximum = 100; $track.SmallChange = 5; $track.LargeChange = 10; $track.TickFrequency = 10
$track.Width = (Px 200)
$track.AutoSize = $false; $track.Height = (Px 34)
$track.Margin = New-Object System.Windows.Forms.Padding((Px 4), (Px 6), 0, 0)
$track.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 34)
$lblLimit = New-Lbl '100%' $cText
$lblLimit.Width = (Px 44); $lblLimit.AutoSize = $false
$btnApply = New-Btn 'Aplicar' 90 $cText
$lblCurrent = New-Lbl '' $cText

$top.Controls.AddRange(@((New-Lbl 'Perfis:'), $btnCold, $btnSilent, $btnNormal, $btnMax, $btnSamsung))
$top.SetFlowBreak($btnSamsung, $true)
$top.Controls.AddRange(@((New-Lbl 'Turbo Boost:'), $cmbTurbo, (New-Lbl 'Limite da CPU:'), $track, $lblLimit, $btnApply, $lblCurrent))
$form.Controls.Add($top)

$status = New-Object System.Windows.Forms.StatusStrip
$status.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 34)
$stInfo = New-Object System.Windows.Forms.ToolStripStatusLabel
$stInfo.ForeColor = $cDim
$stInfo.Text = 'Coletando dados...'
[void]$status.Items.Add($stInfo)
$form.Controls.Add($status)

function Read-Power {
    $script:turbo = Get-PowerValue PERFBOOSTMODE
    $script:limit = Get-PowerValue PROCTHROTTLEMAX
    if ($null -ne $script:turbo -and $TurboOrder -contains $script:turbo) { $cmbTurbo.SelectedIndex = [array]::IndexOf($TurboOrder, [int]$script:turbo) }
    if ($null -ne $script:limit) { $track.Value = [math]::Max(50, [math]::Min(100, [int]$script:limit)); $lblLimit.Text = "$($track.Value)%" }
    $tn = if ($TurboNames.ContainsKey([int]$script:turbo)) { $TurboNames[[int]$script:turbo] } else { "valor $script:turbo" }
    $lblCurrent.Text = "Ativo agora: turbo $tn, limite $script:limit%"
}

$track.Add_ValueChanged({ $lblLimit.Text = "$($track.Value)%" })
$btnApply.Add_Click({ Apply-Power $TurboOrder[$cmbTurbo.SelectedIndex] $track.Value })
$btnCold.Add_Click({ Apply-Power 0 100 })
$btnSilent.Add_Click({ Apply-Power 0 80 })
$btnNormal.Add_Click({ Apply-Power 3 100 })
$btnMax.Add_Click({ Apply-Power 2 100 })
$btnSamsung.Add_Click({ Start-Process explorer.exe $SamsungSettingsApp })

function Format-Bytes([double]$b) {
    if ($b -ge 1GB) { return '{0:N1} GB' -f ($b / 1GB) }
    if ($b -ge 1MB) { return '{0:N1} MB' -f ($b / 1MB) }
    return '{0:N0} KB' -f ($b / 1KB)
}

$script:lastSeq = -1
function Update-Ui {
    $pwr = [System.Windows.Forms.SystemInformation]::PowerStatus
    $batPct = [math]::Round($pwr.BatteryLifePercent * 100)
    $plug = $pwr.PowerLineStatus -eq 'Online'
    $batTxt = if ($plug) { if ($pwr.BatteryChargeStatus -band [System.Windows.Forms.BatteryChargeStatus]::Charging) { 'Na tomada, carregando' } else { 'Na tomada' } } else { $m = $pwr.BatteryLifeRemaining; if ($m -gt 0) { 'Na bateria, ~{0:N0}h{1:D2} restantes' -f [math]::Floor($m / 3600), [int](($m % 3600) / 60) } else { 'Na bateria' } }

    $d = $sync.Data
    if (-not $d -or $sync.Seq -eq $script:lastSeq) {
        if ($sync.Err) { $stInfo.Text = "Erro na coleta: $($sync.Err)" }
        return
    }
    $script:lastSeq = $sync.Seq

    $tMax = ($d.Temps | Measure-Object -Maximum).Maximum
    $tColor = if ($tMax -ge 90) { $cRed } elseif ($tMax -ge 75) { $cOrange } else { $cGreen }
    $tState = if ($tMax -ge 90) { 'quente demais' } elseif ($tMax -ge 75) { 'quente' } else { 'ok' }
    Push-Card 'temp' $tMax ('{0:N0} °C' -f $tMax) ("Sensores: $(($d.Temps | ForEach-Object { '{0:N0}°' -f $_ }) -join ' / ')  ($tState)") $tColor

    $turboOn = $d.CpuPerf -gt 101
    Push-Card 'cpu' $d.CpuUse ('{0:N0}%' -f $d.CpuUse) ('{0:N2} GHz  ({1:N0}% da base{2})' -f $d.CpuGHz, $d.CpuPerf, $(if ($turboOn) { ', em turbo' } else { '' }))
    Push-Card 'gpu' $d.Gpu ('{0:N0}%' -f $d.Gpu) ('3D {0:N0}%   Video {1:N0}%   Mem {2}' -f $d.Gpu3D, $d.GpuVid, (Format-Bytes $d.GpuMem))
    $ramPct = if ($d.RamTot -gt 0) { $d.RamUsed / $d.RamTot * 100 } else { 0 }
    Push-Card 'ram' $ramPct ('{0:N0}%' -f $ramPct) ('{0} de {1} em uso' -f (Format-Bytes $d.RamUsed), (Format-Bytes $d.RamTot))
    Push-Card 'disk' $d.DiskAct ('{0:N0}%' -f $d.DiskAct) ('Ler {0}/s  Gravar {1}/s  C: {2} livre' -f (Format-Bytes $d.DiskRd), (Format-Bytes $d.DiskWr), (Format-Bytes $d.CFree))
    Push-Card 'bat' $batPct ('{0:N0}%' -f $batPct) $batTxt

    $stInfo.Text = 'Atualizado {0:HH:mm:ss}   |   Ventoinha: controlada pelo firmware Samsung (sem leitura de RPM no Windows)' -f (Get-Date)
}

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({ try { Update-Ui } catch { $stInfo.Text = "Erro: $_" } })

$form.Add_Shown({ Read-Power; $timer.Start() })
$form.Add_FormClosing({
    $timer.Stop()
    $sync.Stop = $true
})

[void]$form.ShowDialog()
try { [void]$ps.EndInvoke($psHandle) } catch {}
$ps.Dispose(); $rs.Dispose()
$mutex.ReleaseMutex()
