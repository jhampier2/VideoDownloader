using Spectre.Console;
using VideoDownloader;

// ─── Configuración ─────────────────────────────────────────────────────────────
const string VERSION = "1.0.3";
string outputDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "Videos", "VideoDownloader");

static string E(string s) => Markup.Escape(s);

// ─── Banner ───────────────────────────────────────────────────────────────────
void ShowBanner()
{
    Console.Clear();

    AnsiConsole.Write(new FigletText("VidDown Pro")
        .Centered()
        .Color(Color.DeepSkyBlue1));

    var meta = new Table()
        .HideHeaders()
        .NoBorder()
        .AddColumn(new TableColumn("").Centered())
        .AddColumn(new TableColumn("").Centered())
        .AddColumn(new TableColumn("").Centered());

    meta.AddRow(
        $"[grey]versión[/]  [bold deepskyblue1]{VERSION}[/]",
        "[grey]motor[/]  [bold aqua]yt-dlp[/]",
        "[bold green]+1000[/] [grey]sitios compatibles[/]");

    AnsiConsole.Write(new Align(meta, HorizontalAlignment.Center));
    AnsiConsole.Write(new Rule().RuleStyle("grey dim"));
    AnsiConsole.WriteLine();
}

// ─── Verificar dependencias ───────────────────────────────────────────────────
void CheckDepsAndWarn()
{
    var (hasYtDlp, hasFfmpeg) = DownloadEngine.CheckDependencies();

    var grid = new Grid()
        .AddColumn(new GridColumn().Width(16))
        .AddColumn(new GridColumn().Width(30));

    grid.AddRow("  [bold]yt-dlp[/]",
        hasYtDlp  ? "[green]● Instalado[/]" : "[red]✗ No encontrado[/]");
    grid.AddRow("  [bold]ffmpeg[/]",
        hasFfmpeg ? "[green]● Instalado[/]" : "[yellow]◎ Opcional (recomendado)[/]");

    AnsiConsole.Write(new Align(grid, HorizontalAlignment.Center));
    AnsiConsole.WriteLine();

    if (!hasYtDlp)
    {
        AnsiConsole.Write(new Panel(
            "[red bold]yt-dlp[/] no está instalado en este sistema.\n\n" +
            "[grey dim]Instálalo con:[/]\n\n" +
            "  [grey]Windows :[/]  [aqua]winget install yt-dlp[/]\n" +
            "  [grey]Linux   :[/]  [aqua]pip install yt-dlp[/]\n" +
            "  [grey]macOS   :[/]  [aqua]brew install yt-dlp[/]")
        {
            Header      = new PanelHeader("[red bold] ✗ Dependencia requerida [/]"),
            Border      = BoxBorder.Rounded,
            BorderStyle = Style.Parse("red"),
            Padding     = new Padding(2, 1),
            Expand      = false
        });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey dim]Presiona cualquier tecla para salir...[/]");
        Console.ReadKey(true);
        Environment.Exit(1);
    }
}

// ─── Menú principal ────────────────────────────────────────────────────────────
string ShowMainMenu()
{
    AnsiConsole.MarkupLine($"  [grey dim]📁 {E(outputDir)}[/]");
    AnsiConsole.WriteLine();

    return AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[bold deepskyblue1]¿Qué deseas hacer?[/]")
            .PageSize(14)
            .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
            .AddChoiceGroup("[deepskyblue1 bold]Descargas[/]",
                "⬇   Descargar  (detecta automáticamente)",
                "🎵  Solo audio  (MP3/M4A)")
            .AddChoiceGroup("[grey]Herramientas[/]",
                "🔍  Información del video",
                "📁  Cambiar carpeta de descarga",
                "🕓  Historial de descargas")
            .AddChoiceGroup("[grey]Sistema[/]",
                "🔄  Actualizar yt-dlp",
                "❌  Salir"));
}

// ─── Seleccionar calidad ──────────────────────────────────────────────────────
(QualityPreset preset, string label) SelectQuality()
{
    AnsiConsole.WriteLine();

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("  [bold]Calidad de descarga:[/]")
            .PageSize(8)
            .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
            .AddChoices(
                "🏆  Mejor calidad      — máxima resolución disponible",
                "🎬  1080p Full HD      — ideal para TV o monitor",
                "📺  720p HD            — buena calidad, menor tamaño",
                "📱  480p SD            — ligero, apto para móvil",
                "🎯  Formato manual     — argumento yt-dlp avanzado"));

    return choice switch
    {
        var s when s.Contains("Mejor")   => (QualityPreset.Best,   "Mejor calidad"),
        var s when s.Contains("1080")    => (QualityPreset.HD1080, "1080p FHD"),
        var s when s.Contains("720")     => (QualityPreset.HD720,  "720p HD"),
        var s when s.Contains("480")     => (QualityPreset.SD480,  "480p SD"),
        var s when s.Contains("manual")  => (QualityPreset.Custom, "Personalizado"),
        _                                => (QualityPreset.Best,   "Mejor calidad")
    };
}

// ─── Estimación de tamaño ─────────────────────────────────────────────────────
// Cruza el preset elegido contra los formatos reales del video y suma tamaños.
// Devuelve null si no hay datos de tamaño disponibles.
string? EstimateSize(VideoInfo info, QualityPreset preset)
{
    if (info.Formats is null) return null;

    if (preset == QualityPreset.AudioOnly)
    {
        // Solo la pista de audio de mayor tamaño
        var best = info.Formats
            .Where(f => !f.HasVideo && f.HasAudio && f.BestFilesize.HasValue)
            .OrderByDescending(f => f.BestFilesize)
            .FirstOrDefault();

        return best?.BestFilesize is long ab
            ? FormatBytes(ab) + "  [grey dim](solo audio)[/]"
            : null;
    }

    // Altura máxima según preset
    int? maxH = preset switch
    {
        QualityPreset.HD1080 => 1080,
        QualityPreset.HD720  => 720,
        QualityPreset.SD480  => 480,
        _                    => null   // Best → sin límite
    };

    // Buscar mejor pista de video dentro del límite de altura
    var videoTrack = info.Formats
        .Where(f => f.HasVideo && !f.HasAudio
                    && f.Height.HasValue
                    && (maxH is null || f.Height <= maxH)
                    && f.BestFilesize.HasValue)
        .OrderByDescending(f => f.Height)
        .ThenByDescending(f => f.BestFilesize)
        .FirstOrDefault();

    // Buscar mejor pista de audio (sin video)
    var audioTrack = info.Formats
        .Where(f => !f.HasVideo && f.HasAudio && f.BestFilesize.HasValue)
        .OrderByDescending(f => f.BestFilesize)
        .FirstOrDefault();

    // Si hay formatos combinados (video+audio en un solo stream), usarlos como fallback
    var combinedTrack = info.Formats
        .Where(f => f.HasVideo && f.HasAudio
                    && f.Height.HasValue
                    && (maxH is null || f.Height <= maxH)
                    && f.BestFilesize.HasValue)
        .OrderByDescending(f => f.Height)
        .ThenByDescending(f => f.BestFilesize)
        .FirstOrDefault();

    long total = 0;
    string detail = "";

    if (videoTrack?.BestFilesize is long vb && audioTrack?.BestFilesize is long aub)
    {
        total  = vb + aub;
        detail = $"  [grey dim]{videoTrack.VcodecShort} + {audioTrack.AcodecShort}[/]";
    }
    else if (combinedTrack?.BestFilesize is long cb)
    {
        total  = cb;
        detail = $"  [grey dim]{combinedTrack.VcodecShort} + {combinedTrack.AcodecShort}[/]";
    }
    else return null;

    // Indicar si es aproximado (al menos uno usó filesize_approx)
    bool isApprox = (videoTrack is not null && videoTrack.Filesize is null)
                 || (audioTrack is not null && audioTrack.Filesize is null)
                 || (combinedTrack is not null && combinedTrack.Filesize is null);

    return (isApprox ? "~" : "") + FormatBytes(total) + detail;
}

static string FormatBytes(long bytes) =>
    bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:F2} GB"
  : bytes >= 1_048_576     ? $"{bytes / 1_048_576.0:F1} MB"
  : $"{bytes / 1024.0:F0} KB";

// ─── Tarjeta de info de video ─────────────────────────────────────────────────
void ShowVideoCard(VideoInfo info, string qualityLabel, string? sizeEstimate = null)
{
    var title = info.Title.Length > 65 ? info.Title[..62] + "…" : info.Title;

    // Línea de tamaño: solo se muestra si hay estimación disponible
    var sizeLine = sizeEstimate is not null
        ? $"\n  [grey]Tamaño  :[/]  [bold yellow]{sizeEstimate}[/]"
        : "";

    var content =
        $"[bold white]{E(title)}[/]\n\n" +
        $"  [grey]Canal   :[/]  [white]{E(info.Uploader)}[/]\n" +
        $"  [grey]Duración:[/]  [white]{info.DurationFormatted}[/]    " +
        $"[grey]Vistas:[/]  [white]{info.ViewsFormatted}[/]\n" +
        $"  [grey]Calidad :[/]  [deepskyblue1 bold]{E(qualityLabel)}[/]" +
        sizeLine;

    AnsiConsole.Write(new Panel(content)
    {
        Header      = new PanelHeader("[deepskyblue1 bold] 🎬 Video encontrado [/]"),
        Border      = BoxBorder.Rounded,
        BorderStyle = Style.Parse("deepskyblue1 dim"),
        Padding     = new Padding(1, 1),
        Expand      = false
    });

    AnsiConsole.WriteLine();
}

// ─── Descargar un video ────────────────────────────────────────────────────────
async Task DownloadVideo(bool audioOnly = false, string? url = null)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule(
        audioOnly ? "[deepskyblue1]Descargar audio[/]" : "[deepskyblue1]Descargar video[/]")
        .RuleStyle("deepskyblue1 dim")
        .LeftJustified());
    AnsiConsole.WriteLine();

    // Si ya viene la URL desde DownloadAuto, no volver a pedirla
    if (url is null)
    {
        url = AnsiConsole.Ask<string>("  [bold]URL:[/] ").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            AnsiConsole.MarkupLine("  [red]✗ URL inválida.[/]");
            return;
        }
    }
    else
    {
        AnsiConsole.MarkupLine($"  [grey]URL:[/]  [grey dim]{E(url)}[/]");
    }

    QualityPreset preset;
    string label;

    if (audioOnly)
    {
        (preset, label) = (QualityPreset.AudioOnly, "Solo audio");
        AnsiConsole.MarkupLine(
            "  [grey]Formato:[/]  [deepskyblue1]Mejor audio disponible (MP3)[/]");
    }
    else
    {
        (preset, label) = SelectQuality();
    }

    string formatArg = preset == QualityPreset.Custom
        ? AnsiConsole.Ask<string>("  [grey]Formato yt-dlp:[/] ")
        : DownloadEngine.BuildFormatArg(preset);

    AnsiConsole.WriteLine();
    VideoInfo? info = null;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots2)
        .SpinnerStyle(Style.Parse("deepskyblue1"))
        .StartAsync("  [grey]Obteniendo información del video...[/]", async _ =>
        {
            info = await DownloadEngine.GetVideoInfoAsync(url);
        });

    if (info is not null)
    {
        var sizeEstimate = preset != QualityPreset.Custom
            ? EstimateSize(info, preset)
            : null;
        ShowVideoCard(info, label, sizeEstimate);
    }

    if (!AnsiConsole.Confirm("  ¿Iniciar descarga?"))
        return;

    AnsiConsole.WriteLine();

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        AnsiConsole.MarkupLine("\n  [yellow]⚠ Cancelando descarga...[/]");
    };

    bool success = false;
    var  logs    = new List<string>();

    // ── MEJORA 2: Progreso en dos fases ───────────────────────────────────────
    // Fase 1 → barra de descarga con % real
    // Fase 2 → tarea separada que se activa cuando ffmpeg post-procesa
    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .AutoClear(false)
        .HideCompleted(false)
        .Columns(
            new TaskDescriptionColumn { Alignment = Justify.Left },
            new ProgressBarColumn(),
            new PercentageColumn(),
            new SpinnerColumn(Spinner.Known.Dots))
        .StartAsync(async ctx =>
        {
            var dlTask = ctx.AddTask(
                "[deepskyblue1]⬇ Descargando[/]", maxValue: 100);

            // La tarea de ffmpeg empieza oculta (maxValue:1, Value:0)
            var ppTask = ctx.AddTask(
                "[yellow]⚙ Procesando con ffmpeg[/]", maxValue: 1);
            ppTask.IsIndeterminate = true;
            ppTask.StopTask();   // no visible hasta que se active

            success = await DownloadEngine.DownloadAsync(
                url, outputDir, formatArg,

                // Callback fase 1: actualizar barra de descarga
                progress =>
                {
                    dlTask.Description =
                        $"[deepskyblue1]⬇ Descargando[/]  " +
                        $"[grey dim]{E(progress.Size)} · {E(progress.Speed)} · ETA {E(progress.Eta)}[/]";
                    dlTask.Value = progress.Percent;
                },

                // Callback fase 2: ffmpeg está trabajando
                postLine =>
                {
                    if (!ppTask.IsStarted)
                    {
                        dlTask.Value       = 100;
                        dlTask.Description = "[deepskyblue1]⬇ Descarga completada[/]";
                        ppTask.StartTask();
                    }

                    // Describir qué está haciendo ffmpeg en ese momento
                    var action = postLine switch
                    {
                        var l when l.Contains("[Merger]")         => "Fusionando video + audio",
                        var l when l.Contains("[ExtractAudio]")   => "Extrayendo audio",
                        var l when l.Contains("[EmbedThumbnail]") => "Incrustando miniatura",
                        var l when l.Contains("[Metadata]")       => "Escribiendo metadatos",
                        var l when l.Contains("[FixupM3u8]")      => "Corrigiendo stream HLS",
                        _                                         => "Procesando…"
                    };
                    ppTask.Description = $"[yellow]⚙ ffmpeg:[/]  [grey dim]{action}[/]";
                },

                log => logs.Add(log),
                cts.Token,
                audioOnly: audioOnly);

            // Asegurar que ambas tareas terminen visualmente al 100%
            dlTask.Value = 100;
            if (ppTask.IsStarted) ppTask.Value = 1;
        });

    AnsiConsole.WriteLine();

    if (success)
    {
        AnsiConsole.Write(new Panel(
            $"[green bold]Descarga completada con éxito.[/]\n\n" +
            $"  [grey]Guardado en:[/]  [aqua]{E(outputDir)}[/]")
        {
            Header      = new PanelHeader("[green bold]  ✓ Completado  [/]"),
            Border      = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding     = new Padding(2, 1),
            Expand      = false
        });

        HistoryManager.Add(new DownloadRecord
        {
            Title      = info?.Title ?? url,
            Url        = url,
            Format     = label,
            OutputPath = outputDir,
            Success    = true
        });
    }
    else
    {
        var errorLines = logs
            .TakeLast(4)
            .Select(l => $"  [red dim]›[/] [grey]{E(l.Length > 78 ? l[..75] + "…" : l)}[/]");

        AnsiConsole.Write(new Panel(
            "[red bold]La descarga no se completó.[/]" +
            (logs.Any()
                ? "\n\n[grey dim]Últimos mensajes:[/]\n" + string.Join("\n", errorLines)
                : ""))
        {
            Header      = new PanelHeader("[red bold]  ✗ Error en la descarga  [/]"),
            Border      = BoxBorder.Rounded,
            BorderStyle = Style.Parse("red dim"),
            Padding     = new Padding(2, 1),
            Expand      = false
        });
    }
}

// ─── Descargar playlist ────────────────────────────────────────────────────────
async Task DownloadPlaylist(string? url = null)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[deepskyblue1]Descargar playlist[/]")
        .RuleStyle("deepskyblue1 dim")
        .LeftJustified());
    AnsiConsole.WriteLine();

    if (url is null)
    {
        url = AnsiConsole.Ask<string>("  [bold]URL de la playlist:[/] ").Trim();
        if (string.IsNullOrWhiteSpace(url)) return;
    }
    else
    {
        AnsiConsole.MarkupLine($"  [grey]URL:[/]  [grey dim]{E(url)}[/]");
    }

    var (preset, label) = SelectQuality();
    string formatArg   = DownloadEngine.BuildFormatArg(preset);
    string playlistDir = Path.Combine(outputDir, $"Playlist_{DateTime.Now:yyyyMMdd_HHmmss}");

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Panel(
        $"  [grey]Calidad :[/]  [deepskyblue1 bold]{E(label)}[/]\n" +
        $"  [grey]Destino :[/]  [aqua]{E(playlistDir)}[/]")
    {
        Header      = new PanelHeader("[bold] Resumen [/]"),
        Border      = BoxBorder.Rounded,
        BorderStyle = Style.Parse("grey dim"),
        Padding     = new Padding(1, 0),
        Expand      = false
    });
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm("  ¿Descargar playlist completa?")) return;
    AnsiConsole.WriteLine();

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    int ok = 0, fail = 0;

    await AnsiConsole.Live(new Markup("[grey]  Iniciando...[/]"))
        .StartAsync(async ctx =>
        {
            (ok, fail) = await DownloadEngine.DownloadPlaylistAsync(
                url, playlistDir, formatArg,
                progress => ctx.UpdateTarget(new Markup(
                    $"  [deepskyblue1]⬇[/]  [white]{E(progress)}[/]")),
                log =>
                {
                    var safe = E(log.Length > 82 ? log[..79] + "…" : log);
                    ctx.UpdateTarget(new Markup($"  [grey dim]{safe}[/]"));
                },
                cts.Token);
        });

    AnsiConsole.WriteLine();

    var resultColor = ok > 0 && fail == 0 ? "green" : ok > 0 ? "yellow" : "red";

    AnsiConsole.Write(new Panel(
        $"  [green]✓ Completados :[/]  [bold white]{ok}[/]\n" +
        $"  [red]✗ Errores     :[/]  [bold white]{fail}[/]\n\n" +
        $"  [grey]Carpeta:[/]  [aqua]{E(playlistDir)}[/]")
    {
        Header      = new PanelHeader("[bold] Resultado de playlist [/]"),
        Border      = BoxBorder.Rounded,
        BorderStyle = Style.Parse($"{resultColor} dim"),
        Padding     = new Padding(1, 1),
        Expand      = false
    });
}

// ─── Información del video ─────────────────────────────────────────────────────
async Task ShowVideoInfo()
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[deepskyblue1]Información del video[/]")
        .RuleStyle("deepskyblue1 dim")
        .LeftJustified());
    AnsiConsole.WriteLine();

    string url = AnsiConsole.Ask<string>("  [bold]URL:[/] ").Trim();

    VideoInfo? info = null;
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots2)
        .SpinnerStyle(Style.Parse("deepskyblue1"))
        .StartAsync("  [grey]Analizando video...[/]", async _ =>
        {
            info = await DownloadEngine.GetVideoInfoAsync(url);
        });

    if (info is null)
    {
        AnsiConsole.MarkupLine("\n  [red]✗ No se pudo obtener información del video.[/]");
        return;
    }

    AnsiConsole.WriteLine();

    // ── Panel de metadatos generales ──────────────────────────────────────────
    var snippet = info.DescriptionSnippet;
    var descLine = string.IsNullOrEmpty(snippet)
        ? ""
        : $"\n\n  [grey dim]{E(snippet)}[/]";

    AnsiConsole.Write(new Panel(
        $"[bold white]{E(info.Title)}[/]\n\n" +
        $"  [grey]Canal    :[/]  [white]{E(info.Uploader)}[/]\n" +
        $"  [grey]Duración :[/]  [white]{info.DurationFormatted}[/]    " +
        $"[grey]Publicado:[/]  [white]{info.UploadDateFormatted}[/]\n" +
        $"  [grey]Vistas   :[/]  [white]{info.ViewsFormatted}[/]    " +
        $"[grey]Likes:[/]  [white]{info.LikesFormatted}[/]\n" +
        $"  [grey]Mejor res:[/]  [deepskyblue1 bold]{info.MaxResolution}[/]" +
        descLine)
    {
        Header      = new PanelHeader("[deepskyblue1 bold] 🎬 Información del video [/]"),
        Border      = BoxBorder.Rounded,
        BorderStyle = Style.Parse("deepskyblue1 dim"),
        Padding     = new Padding(1, 1),
        Expand      = false
    });

    // ── MEJORA 1: Tabla detallada de formatos disponibles ─────────────────────
    if (info.Formats is not null)
    {
        AnsiConsole.WriteLine();

        // Separar formatos combinados (video+audio) de los solo-video y solo-audio
        var combined = info.Formats
            .Where(f => f.HasVideo && f.HasAudio && f.Height.HasValue)
            .DistinctBy(f => f.Resolution)
            .OrderByDescending(f => f.Height)
            .Take(6)
            .ToList();

        var videoOnly = info.Formats
            .Where(f => f.HasVideo && !f.HasAudio && f.Height.HasValue)
            .DistinctBy(f => f.Resolution)
            .OrderByDescending(f => f.Height)
            .Take(6)
            .ToList();

        var audioOnly2 = info.Formats
            .Where(f => !f.HasVideo && f.HasAudio)
            .DistinctBy(f => f.AcodecShort)
            .Take(4)
            .ToList();

        // ── Tabla de formatos de video ────────────────────────────────────────
        var formatTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold grey] Formatos disponibles [/]")
            .AddColumn(new TableColumn("[grey]Resolución[/]").Centered().Width(12))
            .AddColumn(new TableColumn("[grey]FPS[/]").Centered().Width(6))
            .AddColumn(new TableColumn("[grey]Video[/]").Centered().Width(8))
            .AddColumn(new TableColumn("[grey]Audio[/]").Centered().Width(8))
            .AddColumn(new TableColumn("[grey]Tamaño[/]").RightAligned().Width(12))
            .AddColumn(new TableColumn("[grey]Tipo[/]").Centered().Width(14));

        // Formatos combinados (v+a)
        foreach (var f in combined)
        {
            formatTable.AddRow(
                $"[deepskyblue1 bold]{E(f.Resolution ?? "?")}[/]",
                f.Fps.HasValue ? $"[white]{f.Fps.Value:F0}[/]" : "[grey]—[/]",
                $"[aqua]{E(f.VcodecShort)}[/]",
                $"[aqua]{E(f.AcodecShort)}[/]",
                $"[white]{E(f.FilesizeFormatted)}[/]",
                "[green dim]video+audio[/]");
        }

        // Formatos solo-video
        foreach (var f in videoOnly)
        {
            formatTable.AddRow(
                $"[deepskyblue1]{E(f.Resolution ?? "?")}[/]",
                f.Fps.HasValue ? $"[white]{f.Fps.Value:F0}[/]" : "[grey]—[/]",
                $"[aqua]{E(f.VcodecShort)}[/]",
                "[grey]—[/]",
                $"[grey]{E(f.FilesizeFormatted)}[/]",
                "[grey dim]solo video[/]");
        }

        // Formatos solo-audio
        foreach (var f in audioOnly2)
        {
            formatTable.AddRow(
                "[grey]audio[/]",
                "[grey]—[/]",
                "[grey]—[/]",
                $"[aqua]{E(f.AcodecShort)}[/]",
                $"[grey]{E(f.FilesizeFormatted)}[/]",
                "[yellow dim]solo audio[/]");
        }

        AnsiConsole.Write(formatTable);
    }
}

// ─── Historial ─────────────────────────────────────────────────────────────────
void ShowHistory()
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[grey]Historial de descargas[/]")
        .RuleStyle("grey dim")
        .LeftJustified());
    AnsiConsole.WriteLine();

    var records = HistoryManager.Load();

    if (!records.Any())
    {
        AnsiConsole.MarkupLine("  [grey dim]No hay descargas registradas todavía.[/]");
        return;
    }

    // ── MEJORA 3: Tabla con URL y carpeta destino ─────────────────────────────
    var table = new Table()
        .Border(TableBorder.Simple)
        .BorderColor(Color.Grey)
        .AddColumn(new TableColumn("[grey]#[/]").Width(4).Centered())
        .AddColumn(new TableColumn("[grey]Fecha[/]").Width(14).Centered())
        .AddColumn(new TableColumn("[grey]Título[/]"))
        .AddColumn(new TableColumn("[grey]Calidad[/]").Width(13).Centered())
        .AddColumn(new TableColumn("[grey]Estado[/]").Width(9).Centered());

    int idx = 1;
    foreach (var r in records.Take(15))
    {
        var title = r.Title.Length > 38 ? r.Title[..35] + "…" : r.Title;
        table.AddRow(
            $"[grey dim]{idx++}[/]",
            $"[grey dim]{r.Date:dd/MM/yy HH:mm}[/]",
            E(title),
            $"[aqua]{E(r.Format)}[/]",
            r.Success ? "[green]✓ OK[/]" : "[red]✗ Error[/]");
    }

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"  [grey dim]{records.Count} entradas en total.[/]");
    AnsiConsole.WriteLine();

    // Ver detalle de una entrada específica
    if (records.Count > 0 && AnsiConsole.Confirm(
        "  [grey]¿Ver detalle de alguna descarga?[/]", defaultValue: false))
    {
        int num = AnsiConsole.Ask<int>("  [grey]Número de entrada (1-{0}):[/] "
            .Replace("{0}", Math.Min(records.Count, 15).ToString()));

        num = Math.Clamp(num, 1, Math.Min(records.Count, 15));
        var r = records[num - 1];

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            $"  [grey]Título  :[/]  [white]{E(r.Title)}[/]\n" +
            $"  [grey]URL     :[/]  [aqua]{E(r.Url)}[/]\n" +
            $"  [grey]Calidad :[/]  [white]{E(r.Format)}[/]\n" +
            $"  [grey]Carpeta :[/]  [aqua]{E(r.OutputPath)}[/]\n" +
            $"  [grey]Fecha   :[/]  [white]{r.Date:dd/MM/yyyy HH:mm:ss}[/]\n" +
            $"  [grey]Estado  :[/]  " +
            (r.Success ? "[green bold]✓ Completada[/]" : "[red bold]✗ Error[/]"))
        {
            Header      = new PanelHeader($"[bold] Detalle #{num} [/]"),
            Border      = BoxBorder.Rounded,
            BorderStyle = Style.Parse("grey"),
            Padding     = new Padding(1, 1),
            Expand      = false
        });
    }

    AnsiConsole.WriteLine();
    if (AnsiConsole.Confirm("  [grey]¿Limpiar historial?[/]", defaultValue: false))
    {
        HistoryManager.Clear();
        AnsiConsole.MarkupLine("  [green]✓ Historial limpiado.[/]");
    }
}

// ─── Actualizar yt-dlp ────────────────────────────────────────────────────────
async Task UpdateYtDlp()
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[grey]Actualizar yt-dlp[/]")
        .RuleStyle("grey dim")
        .LeftJustified());
    AnsiConsole.WriteLine();

    string result = "";
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Circle)
        .SpinnerStyle(Style.Parse("deepskyblue1"))
        .StartAsync("  [grey]Buscando actualización...[/]", async _ =>
        {
            result = await DownloadEngine.UpdateYtDlpAsync();
        });

    var isUpdated  = result.Contains("up to date") || result.Contains("actualizado");
    var panelColor = isUpdated ? "green" : "deepskyblue1";
    var header     = isUpdated
        ? "[green bold] ✓ yt-dlp está al día [/]"
        : "[deepskyblue1 bold] ↑ yt-dlp actualizado [/]";

    AnsiConsole.Write(new Panel($"  [grey]{E(result.Trim())}[/]")
    {
        Header      = new PanelHeader(header),
        Border      = BoxBorder.Rounded,
        BorderStyle = Style.Parse($"{panelColor} dim"),
        Padding     = new Padding(1, 0),
        Expand      = false
    });
}

// ─── Descarga con detección automática ────────────────────────────────────────
async Task DownloadAuto()
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[deepskyblue1]Descargar[/]")
        .RuleStyle("deepskyblue1 dim")
        .LeftJustified());
    AnsiConsole.WriteLine();

    string url = AnsiConsole.Ask<string>("  [bold]URL:[/] ").Trim();
    if (string.IsNullOrWhiteSpace(url))
    {
        AnsiConsole.MarkupLine("  [red]✗ URL inválida.[/]");
        return;
    }

    // ── Detectar tipo de URL ──────────────────────────────────────────────────
    UrlType urlType = UrlType.Unknown;
    int? playlistCount = null;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots2)
        .SpinnerStyle(Style.Parse("deepskyblue1"))
        .StartAsync("  [grey]Detectando tipo de URL...[/]", async _ =>
        {
            (urlType, playlistCount) = await DownloadEngine.DetectUrlTypeAsync(url);
        });

    // ── Mostrar resultado de la detección ────────────────────────────────────
    switch (urlType)
    {
        case UrlType.Video:
            AnsiConsole.MarkupLine("  [green]●[/] [grey]Detectado:[/]  [white]Video individual[/]");
            AnsiConsole.WriteLine();
            await DownloadVideo(url: url);
            break;

        case UrlType.Playlist:
            var countLabel = playlistCount.HasValue
                ? $"[white]{playlistCount} videos[/]"
                : "[white]playlist[/]";
            AnsiConsole.MarkupLine($"  [deepskyblue1]●[/] [grey]Detectado:[/]  📋 Playlist — {countLabel}");
            AnsiConsole.WriteLine();
            await DownloadPlaylist(url: url);
            break;

        case UrlType.Unknown:
        default:
            // No se pudo detectar — preguntar al usuario
            AnsiConsole.MarkupLine(
                "  [yellow]⚠[/] [grey]No se pudo detectar el tipo de URL automáticamente.[/]");
            AnsiConsole.WriteLine();

            var manual = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  [bold]¿Qué quieres descargar?[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(
                        "⬇  Video individual",
                        "📋  Playlist completa"));

            if (manual.Contains("Video"))
                await DownloadVideo(url: url);
            else
                await DownloadPlaylist(url: url);
            break;
    }
}

// ─── MAIN LOOP ─────────────────────────────────────────────────────────────────
ShowBanner();
CheckDepsAndWarn();

while (true)
{
    var choice = ShowMainMenu();

    try
    {
        if      (choice.Contains("Descargar"))  await DownloadAuto();
        else if (choice.Contains("audio"))       await DownloadVideo(audioOnly: true);
        else if (choice.Contains("Información"))     await ShowVideoInfo();
        else if (choice.Contains("carpeta"))
        {
            AnsiConsole.WriteLine();
            string newDir = AnsiConsole.Ask("  [bold]Nueva carpeta de descarga:[/]", outputDir);
            outputDir = newDir;
            Directory.CreateDirectory(outputDir);
            AnsiConsole.MarkupLine(
                $"  [green]✓ Carpeta actualizada:[/]  [aqua]{E(outputDir)}[/]");
        }
        else if (choice.Contains("Historial"))  ShowHistory();
        else if (choice.Contains("Actualizar")) await UpdateYtDlp();
        else if (choice.Contains("Salir"))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [grey dim]¡Hasta luego![/]");
            AnsiConsole.WriteLine();
            break;
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [grey dim]Presiona cualquier tecla para volver al menú...[/]");
    Console.ReadKey(true);
    ShowBanner();
}