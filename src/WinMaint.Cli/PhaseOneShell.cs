using System.Globalization;
using Spectre.Console;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Cli;

internal sealed class WinMaintShell(
    IAnsiConsole console,
    bool plainText,
    ReadOnlyDiagnosticCoordinator diagnosticCoordinator,
    StabilityTestCoordinator stabilityCoordinator,
    IStabilitySessionStore sessionStore,
    PcManagerDetectionCoordinator pcManagerDetector,
    PcManagerActionCoordinator pcManagerActions,
    ReportJournal reports,
    ITemporaryInventoryProvider temporaryInventory,
    ITemporaryCleanupRecoveryReader? cleanupRecovery = null)
{
    private bool reportStarted;
    private bool lastReportSaveSucceeded;
    private ReportSaveResult? lastSaveResult;

    public async Task<int> RunAsync(
        bool smokeTest,
        bool runDiagnostic,
        bool runStability,
        bool confirmWorkSaved,
        bool runPcManagerDetection,
        bool runPcManagerStore,
        bool runReport)
    {
        RenderDashboard(PhaseFiveUiData.CreateDashboard());
        await RenderInterruptedSessionAsync().ConfigureAwait(false);
        await RenderIncompleteCleanupAsync().ConfigureAwait(false);

        if (smokeTest)
        {
            RenderMaintenancePlan(MaintenancePlanBuilder.BuildPreview(
                PhaseFiveUiData.CreateMaintenanceChoices().Take(2).Select(action => action.Id)));
            console.MarkupLine("[green]SMOKE TEST TUI: OK[/]");
            return 0;
        }

        if (runDiagnostic)
        {
            await RunDiagnosticAsync().ConfigureAwait(false);
            await SaveReportAsync("Completed").ConfigureAwait(false);
            return lastSaveResult!.ExportExitCode;
        }

        if (runReport)
        {
            await RunDiagnosticAsync().ConfigureAwait(false);
            await SaveReportAsync("Completed").ConfigureAwait(false);
            PrintReportPaths();
            return lastSaveResult!.ExportExitCode;
        }

        if (runStability)
        {
            return await RunStabilityAsync(confirmWorkSaved).ConfigureAwait(false);
        }

        if (runPcManagerDetection)
        {
            await RunPcManagerDetectionAsync(allowActionPrompt: false).ConfigureAwait(false);
            return 0;
        }

        if (runPcManagerStore)
        {
            await RunPcManagerDetectionAsync(allowActionPrompt: true, openStorePage: true).ConfigureAwait(false);
            return 0;
        }

        while (true)
        {
            var option = plainText ? PromptPlainTextMenu() : PromptAnsiMenu();

            if (option.Id == "exit")
            {
                if (reportStarted)
                {
                    await SaveReportAsync("Completed").ConfigureAwait(false);
                }

                console.MarkupLine("[grey]Salida segura. Los reportes locales registran las operaciones de esta sesión.[/]");
                return reportStarted && lastSaveResult is { Succeeded: false } ? 4 : 0;
            }

            if (option.Id == "cleanup")
            {
                await RunTemporaryInventoryAsync().ConfigureAwait(false);
            }
            else if (option.Id is "maintenance" or "integrity")
            {
                RenderMaintenanceSelection(option.Id);
            }
            else if (option.Id == "diagnostic")
            {
                await RunDiagnosticAsync().ConfigureAwait(false);
            }
            else if (option.Id == "stability")
            {
                await RunStabilityAsync(workSavedConfirmed: false).ConfigureAwait(false);
            }
            else if (option.Id == "pc-manager")
            {
                await RunPcManagerDetectionAsync(allowActionPrompt: true).ConfigureAwait(false);
            }
            else if (option.Id == "reports")
            {
                if (!reportStarted)
                {
                    await RunDiagnosticAsync().ConfigureAwait(false);
                }

                await SaveReportAsync("Partial").ConfigureAwait(false);
                PrintReportPaths();
            }
            else
            {
                RenderUnavailable(option);
            }
        }
    }

    private void RenderDashboard(DemoDashboardSnapshot snapshot)
    {
        var tableBorder = plainText ? TableBorder.Ascii : TableBorder.Rounded;
        var panelBorder = plainText ? BoxBorder.Ascii : BoxBorder.Rounded;
        console.MarkupLine("[bold]Hecho por Jafet[/]");
        console.Write(new Rule("[bold cyan]WinMaint v1[/]").RuleStyle("cyan"));
        console.Write(new Panel($"[bold yellow]{Markup.Escape(snapshot.Banner)}[/]")
            .Border(panelBorder)
            .Header("[yellow]ESTADO[/]"));

        var status = new Grid();
        status.AddColumn();
        status.AddColumn();
        status.AddRow("[bold]Salud[/]", Markup.Escape(snapshot.HealthStatus));
        status.AddRow("[bold]Confianza[/]", Markup.Escape(snapshot.ConfidenceStatus));
        console.Write(status);

        var summary = new Table().Border(tableBorder).Title("[cyan]Tablero del sistema[/]");
        summary.AddColumn("Dato");
        summary.AddColumn("Valor");
        summary.AddColumn("Origen");
        foreach (var item in snapshot.SystemSummary)
        {
            summary.AddRow(
                Markup.Escape(item.Label),
                Markup.Escape(item.Value),
                $"[yellow]{Markup.Escape(item.Origin)}[/]");
        }

        console.Write(summary);

        if (snapshot.Findings.Count > 0)
        {
            var findings = new Table().Border(tableBorder).Title("[cyan]Hallazgos de demostración[/]");
            findings.AddColumn("Prioridad");
            findings.AddColumn("Hallazgo");
            findings.AddColumn("Recomendación");
            findings.AddColumn("Riesgo");
            findings.AddColumn("Origen");
            foreach (var finding in snapshot.Findings)
            {
                findings.AddRow(
                    finding.Priority.ToString(),
                    Markup.Escape(finding.Finding),
                    Markup.Escape(finding.Recommendation),
                    finding.Risk.ToString(),
                    $"[yellow]{Markup.Escape(finding.Origin)}[/]");
            }

            console.Write(findings);
        }

        console.MarkupLine("[grey]Fase 4 completada: temporales validados en Sandbox; mutación de producción bloqueada.[/]");
    }

    private async Task RunPcManagerDetectionAsync(bool allowActionPrompt, bool openStorePage = false)
    {
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            console.MarkupLine("[cyan]Microsoft PC Manager — detección de solo lectura[/]");
            var result = await pcManagerDetector.DetectAsync(cancellationSource.Token).ConfigureAwait(false);
            var table = new Table()
                .Border(plainText ? TableBorder.Ascii : TableBorder.Rounded)
                .AddColumn("Comprobación")
                .AddColumn("Resultado");
            table.AddRow("Compatibilidad", AvailabilityLabel(result.SystemCompatibility));
            table.AddRow("Instalación", InstallationLabel(result.Installation));
            table.AddRow("Versión", Markup.Escape(result.Package?.Version ?? "No disponible"));
            table.AddRow("WinGet", AvailabilityLabel(result.WinGet));
            table.AddRow("Paquete Microsoft Store", AvailabilityLabel(result.MicrosoftStorePackage));
            table.AddRow("Política de Store", StorePolicyLabel(result.MicrosoftStorePolicy));
            table.AddRow("Interfaz de red local", AvailabilityLabel(result.NetworkInterface));
            console.Write(table);
            var plan = openStorePage ? PcManagerPlanBuilder.BuildStorePage(result) : PcManagerPlanBuilder.Build(result);
            reports.AddDecision(result, plan);
            console.MarkupLine($"[cyan]Propuesta:[/] {Markup.Escape(PcManagerActionLabel(plan.Action))} ({plan.Priority}/{plan.Risk})");
            console.MarkupLine($"[grey]{Markup.Escape(plan.Detail)}[/]");
            console.MarkupLine("[grey]La interfaz de red no demuestra acceso a Internet. Los datos desconocidos se conservan como tales.[/]");
            if (!allowActionPrompt || plan.IsBlocked)
            {
                await SaveReportAsync("Partial").ConfigureAwait(false);
                console.MarkupLine("[yellow]Modo de detección: no se inició ninguna acción externa.[/]");
                return;
            }

            var token = PcManagerAuthorizationPolicy.RequiredToken(plan.Action)!;
            if (plan.Action == PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet)
            {
                console.MarkupLine("[yellow]La instalación acepta los acuerdos de la fuente msstore y del paquete, y modifica Windows.[/]");
            }
            else
            {
                console.MarkupLine("[yellow]La acción abrirá una aplicación externa; WinMaint no controlará su interfaz.[/]");
            }

            WritePlainLine($"Escribe exactamente {token} para continuar; Enter cancela: ");
            var suppliedToken = Console.ReadLine();
            var authorization = PcManagerAuthorizationPolicy.Evaluate(plan.Action, suppliedToken);
            var actionResult = await pcManagerActions.ExecuteAsync(
                plan,
                authorization,
                cancellationSource.Token).ConfigureAwait(false);
            reports.AddAction(actionResult);
            await SaveReportAsync("Partial").ConfigureAwait(false);
            console.MarkupLine($"[cyan]Resultado:[/] {actionResult.Outcome}");
            console.MarkupLine($"[grey]{Markup.Escape(actionResult.Detail)}[/]");
            console.MarkupLine($"[grey]{Markup.Escape(actionResult.Verification)}[/]");
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Detección cancelada; no se realizaron cambios.[/]");
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private async Task RunTemporaryInventoryAsync()
    {
        if (reports.CreateReport("Partial").Snapshots.Count == 0)
        {
            await RunDiagnosticAsync().ConfigureAwait(false);
        }

        if (reports.CreateReport("Partial").Snapshots.Count == 0)
        {
            console.MarkupLine("[yellow]Inventario omitido: primero completa el diagnóstico. No se borró nada.[/]");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            console.MarkupLine("[cyan]Inventario de temporales (R0). Ctrl+C cancela. No se eliminará nada.[/]");
            await SaveReportAsync("InProgress").ConfigureAwait(false);
            var inventory = await temporaryInventory.CollectAsync(cancellation.Token).ConfigureAwait(false);
            reports.AddPreparedTemporaryInventory(inventory);
            var summary = new Table().Border(plainText ? TableBorder.Ascii : TableBorder.Rounded)
                .Title("[cyan]Temporales — solo inventario[/]").AddColumn("Dato").AddColumn("Resultado");
            static string Count(long? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "No disponible";
            summary.AddRow("Cobertura", inventory.Coverage switch
            {
                EvidenceCoverage.Complete => "Recorrido terminado",
                EvidenceCoverage.Partial => "Parcial; no autoriza limpieza",
                _ => "No disponible; raíz no admitida o sin datos",
            });
            summary.AddRow("Archivos candidatos", Count(inventory.CandidateFiles));
            summary.AddRow("Tamaño lógico", inventory.CandidateLogicalBytes is long bytes
                ? $"{bytes / 1048576d:0.00} MiB ({Count(bytes)} bytes lógicos)" : "No disponible");
            summary.AddRow("Archivos recientes", Count(inventory.RecentFiles));
            summary.AddRow("Elementos protegidos", Count(inventory.ProtectedItems));
            summary.AddRow("Elementos inaccesibles", Count(inventory.UnavailableItems));

            console.Write(summary);
            console.MarkupLine("[grey]Mínimo 7 días. Es una estimación por metadata; no confirma espacio liberable. No se borró nada.[/]");
            reports.AddNote("Temporales: solo inventario R0. La limpieza de producción permanece bloqueada.");
            await SaveReportAsync("Partial").ConfigureAwait(false);
            console.MarkupLine("[yellow]Eliminación bloqueada: falta aceptación del flujo y del ejecutable final de Fase 8.[/]");
        }
        catch (OperationCanceledException)
        {
            reports.AddNote("Inventario de temporales cancelado; no se publicó un resultado completo ni se borraron archivos.");
            await SaveReportAsync("Partial").ConfigureAwait(false);
            console.MarkupLine("[yellow]Inventario cancelado. No se borró ningún archivo.[/]");
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private async Task RunDiagnosticAsync()
    {
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            await SaveReportAsync("InProgress").ConfigureAwait(false);
            var report = await CollectDiagnosticAsync(cancellationSource.Token).ConfigureAwait(false);
            reports.AddSnapshot(report);
            await SaveReportAsync("Partial").ConfigureAwait(false);
            RenderDiagnosticReport(report);
            RenderHealthAssessment(reports.FinalHealth!);
        }
        catch (OperationCanceledException)
        {
            reports.AddNote("Diagnóstico cancelado: no se obtuvo una captura completa de esta operación.");
            await SaveReportAsync("Partial").ConfigureAwait(false);
            console.MarkupLine("[yellow]Diagnóstico cancelado. No se realizaron cambios.[/]");
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private async Task<DiagnosticReport> CollectDiagnosticAsync(CancellationToken cancellationToken)
    {
        if (plainText)
        {
            WritePlainLine("Recopilando diagnóstico de solo lectura...");
            return await diagnosticCoordinator.CollectAsync(cancellationToken).ConfigureAwait(false);
        }

        return await console
            .Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(
                "[cyan]Recopilando diagnóstico de solo lectura...[/]",
                _ => diagnosticCoordinator.CollectAsync(cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<int> RunStabilityAsync(bool workSavedConfirmed)
    {
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            console.Write(new Rule("[bold cyan]Prueba rápida de estabilidad[/]").RuleStyle("cyan"));
            console.MarkupLine("[yellow]Duración prevista: 4–6 minutos. Esc o Ctrl+C solicita cancelación.[/]");
            var baseline = await CollectDiagnosticAsync(cancellationSource.Token).ConfigureAwait(false);
            reports.AddSnapshot(baseline);
            await SaveReportAsync("InProgress").ConfigureAwait(false);
            var initialAssessment = reports.FinalHealth!;
            RenderHealthAssessment(initialAssessment);
            var preflight = StabilityPreflightEvaluator.Evaluate(baseline, initialAssessment);
            RenderPreflight(preflight);
            if (!preflight.CanRun)
            {
                reports.AddNote("Prueba de estabilidad omitida: bloqueada por preflight.");
                await SaveReportAsync("Partial").ConfigureAwait(false);
                console.MarkupLine("[red]Prueba bloqueada por el preflight. No se generó carga.[/]");
                return 3;
            }

            if (!workSavedConfirmed && !ConfirmWorkSaved())
            {
                reports.AddNote("Prueba de estabilidad cancelada antes de generar carga.");
                await SaveReportAsync("Partial").ConfigureAwait(false);
                console.MarkupLine("[yellow]Prueba no iniciada. No se generó carga.[/]");
                return 2;
            }

            StabilityTestResult result;
            using var monitorStop = new CancellationTokenSource();
            var escapeMonitor = MonitorEscapeAsync(cancellationSource, monitorStop.Token);
            try
            {
                result = plainText
                    ? await RunStabilityPlainAsync(baseline, initialAssessment, cancellationSource.Token).ConfigureAwait(false)
                    : await RunStabilityAnsiAsync(baseline, initialAssessment, cancellationSource.Token).ConfigureAwait(false);
            }
            finally
            {
                monitorStop.Cancel();
                await escapeMonitor.ConfigureAwait(false);
            }

            RenderStabilityResult(result);
            reports.AddStability(result);
            await SaveReportAsync("Partial").ConfigureAwait(false);
            if (!cancellationSource.IsCancellationRequested)
            {
                reports.AddSnapshot(await CollectDiagnosticAsync(cancellationSource.Token).ConfigureAwait(false));
                await SaveReportAsync("Partial").ConfigureAwait(false);
            }
            RenderHealthAssessment(reports.FinalHealth!);
            return result.WasInterrupted ? 130 : !lastReportSaveSucceeded ? 4 :
                result.Probes.Any(probe => probe.Status is StabilityProbeStatus.Failed or StabilityProbeStatus.TimedOut) ? 3 :
                result.Probes.Any(probe => probe.Status == StabilityProbeStatus.Skipped) ? 2 : 0;
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Cancelación registrada. La próxima ejecución advertirá sobre la sesión interrumpida.[/]");
            reports.AddNote("Prueba de estabilidad interrumpida; revisar el estado de sesión recuperable.");
            await SaveReportAsync("Partial").ConfigureAwait(false);
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private Task<StabilityTestResult> RunStabilityPlainAsync(
        DiagnosticReport baseline,
        HealthAssessment assessment,
        CancellationToken cancellationToken)
    {
        var progress = new InlineProgress<StabilityProgress>(item =>
            WritePlainLine($"[{item.Elapsed:mm\\:ss}] {item.Label}: {item.State}"));
        return stabilityCoordinator.RunAsync(baseline, assessment, progress, cancellationToken);
    }

    private Task<StabilityTestResult> RunStabilityAnsiAsync(
        DiagnosticReport baseline,
        HealthAssessment assessment,
        CancellationToken cancellationToken) => console
        .Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[cyan]Preparando prueba controlada...[/]", context =>
        {
            var progress = new InlineProgress<StabilityProgress>(item =>
                context.Status($"[cyan]{Markup.Escape(item.Label)}[/] — {Markup.Escape(item.State)} — {item.Elapsed:mm\\:ss}"));
            return stabilityCoordinator.RunAsync(baseline, assessment, progress, cancellationToken);
        });

    private bool ConfirmWorkSaved()
    {
        if (Console.IsInputRedirected)
        {
            console.MarkupLine("[yellow]Falta confirmación explícita. Repite con --confirm-work-saved después de guardar tu trabajo.[/]");
            return false;
        }

        if (!plainText)
        {
            return console.Confirm("[yellow]¿Guardaste tu trabajo y deseas iniciar la carga controlada?[/]", defaultValue: false);
        }

        WritePlain("¿Guardaste tu trabajo y deseas iniciar? Escribe SI: ");
        return string.Equals(Console.ReadLine()?.Trim(), "SI", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RenderInterruptedSessionAsync()
    {
        var interrupted = await sessionStore.ReadInterruptedAsync(CancellationToken.None).ConfigureAwait(false);
        if (interrupted is null)
        {
            return;
        }

        var started = interrupted.StartedAt == DateTimeOffset.MinValue
            ? "fecha desconocida"
            : interrupted.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        console.MarkupLine($"[yellow]Sesión de estabilidad interrumpida detectada: {Markup.Escape(started)}; última etapa: {Markup.Escape(interrupted.LastCompletedProbeId ?? "ninguna")}.[/]");
        console.MarkupLine("[grey]No se reanuda carga automáticamente; una nueva prueba empieza desde preflight.[/]");
        reports.AddNote($"Sesión anterior de estabilidad interrumpida: {started}; última etapa: {interrupted.LastCompletedProbeId ?? "ninguna"}.");
    }

    private async Task RenderIncompleteCleanupAsync()
    {
        if (cleanupRecovery is null) return;
        try
        {
            var attempts = await cleanupRecovery.ReadAllAsync().ConfigureAwait(false);
            foreach (var attempt in attempts.Where(item => !item.IsClosed))
            {
                console.MarkupLine("[yellow]Se encontró un registro de limpieza incompleto. No se reanuda automáticamente.[/]");
                if (attempt.ToRecord() is { } record)
                {
                    reports.AddTemporaryCleanup(record);
                    console.WriteLine($"Verificados: {record.DeletedFiles}; sin verificar: {record.Files.Count(file => file.Outcome == TemporaryFileOutcome.Unverified)}; no intentados: {record.NotAttemptedCandidates.Count}.");
                }
                else reports.AddNote("Registro de limpieza ilegible: no se presume ausencia de cambios ni se reanuda.");
                if (attempt.IsCorrupt) console.MarkupLine("[yellow]El registro tiene datos corruptos; los resultados restantes son desconocidos.[/]");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reports.AddNote("No se pudo revisar el registro de limpieza; no se presume ausencia de cambios.");
            console.MarkupLine("[yellow]No se pudo revisar el registro de limpieza. La ejecución sigue bloqueada.[/]");
        }
    }

    private async Task SaveReportAsync(string status)
    {
        lastReportSaveSucceeded = false;
        try
        {
            lastSaveResult = await reports.TrySaveAsync(status).ConfigureAwait(false);
            reportStarted = true;
            lastReportSaveSucceeded = lastSaveResult.Succeeded;
            if (!lastSaveResult.Succeeded)
                console.MarkupLine(lastSaveResult.JsonSaved
                    ? "[yellow]JSON guardado; TXT no actualizado. La exportación está incompleta. Los datos siguen en memoria para reintentar.[/]"
                    : "[yellow]No se pudo guardar el reporte. Los datos siguen en memoria para reintentar desde Reportes.[/]");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lastSaveResult = new(false, false, exception.GetType().Name);
            console.MarkupLine($"[yellow]No se pudo guardar el reporte local ({exception.GetType().Name}). Los datos siguen en memoria; revisa permisos o espacio y vuelve a usar Reportes.[/]");
        }
    }

    private void PrintReportPaths()
    {
        if (!lastReportSaveSucceeded)
        {
            console.MarkupLine("[yellow]La última exportación no se completó. No se confirma un par TXT/JSON actualizado.[/]");
            return;
        }

        console.MarkupLine($"[cyan]TXT:[/] {Markup.Escape(reports.TextPath)}");
        console.MarkupLine($"[cyan]JSON:[/] {Markup.Escape(reports.JsonPath)}");
        console.MarkupLine("[grey]Partial = sesión abierta o datos incompletos. InProgress = operación sin cierre registrado; puede haber sido interrumpida. Completed = cierre registrado, no garantía de salud.[/]");
    }

    private static async Task MonitorEscapeAsync(
        CancellationTokenSource cancellationSource,
        CancellationToken stopToken)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        try
        {
            while (!stopToken.IsCancellationRequested && !cancellationSource.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                {
                    cancellationSource.Cancel();
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), stopToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // La prueba terminó y detuvo el monitor de teclado.
        }
        catch (InvalidOperationException)
        {
            // Consola sin acceso a teclado; Ctrl+C sigue disponible.
        }
        catch (IOException)
        {
            // Consola sin acceso a teclado; Ctrl+C sigue disponible.
        }
    }

    private void RenderDiagnosticReport(DiagnosticReport report)
    {
        var border = plainText ? TableBorder.Ascii : TableBorder.Rounded;
        console.Write(new Rule("[bold cyan]Diagnóstico R0[/]").RuleStyle("cyan"));
        console.MarkupLine($"Capturado: [bold]{Markup.Escape(report.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))}[/]");

        foreach (var section in report.Sections)
        {
            var table = new Table().Border(border).Title($"[cyan]{Markup.Escape(section.Title)}[/]");
            table.AddColumn("Dato");
            table.AddColumn("Valor");
            table.AddColumn("Cobertura");
            table.AddColumn("Fuente");

            foreach (var datum in section.Data)
            {
                var value = datum.Detail is null ? datum.Value : $"{datum.Value} — {datum.Detail}";
                table.AddRow(
                    Markup.Escape(datum.Name),
                    Markup.Escape(value),
                    CoverageMarkup(datum.Coverage),
                    Markup.Escape(datum.Source));
            }

            console.Write(table);
            foreach (var note in section.Notes)
            {
                console.MarkupLine($"[grey]Nota: {Markup.Escape(note)}[/]");
            }
        }

        console.MarkupLine($"[bold]Datos desconocidos:[/] {report.UnknownValueCount}");
        console.MarkupLine(report.IsReadOnly
            ? "[green]Diagnóstico terminado en modo R0. No se realizaron cambios.[/]"
            : "[red]El resultado no pudo confirmar el modo de solo lectura.[/]");
    }

    private static string CoverageMarkup(EvidenceCoverage coverage) => coverage switch
    {
        EvidenceCoverage.Complete => "[green]Completa[/]",
        EvidenceCoverage.Partial => "[yellow]Parcial[/]",
        _ => "[red]Ausente[/]",
    };

    private void RenderHealthAssessment(HealthAssessment assessment)
    {
        var border = plainText ? TableBorder.Ascii : TableBorder.Rounded;
        console.Write(new Rule("[bold cyan]Evaluación de salud[/]").RuleStyle("cyan"));
        var overall = assessment.OverallScore is null ? "Desconocida" : $"{assessment.OverallScore}/100 — {assessment.Band}";
        console.MarkupLine($"[bold]Salud:[/] {Markup.Escape(overall)}");
        console.MarkupLine($"[bold]Confianza:[/] {assessment.ConfidencePercent}% — reglas {Markup.Escape(assessment.RuleSetVersion)}");

        var domains = new Table().Border(border);
        domains.AddColumn("Dominio");
        domains.AddColumn("Puntuación");
        domains.AddColumn("Confianza");
        foreach (var domain in assessment.Domains)
        {
            domains.AddRow(
                DomainLabel(domain.Domain),
                domain.Score?.ToString(CultureInfo.InvariantCulture) ?? "Desconocida",
                domain.ConfidencePercent.ToString(CultureInfo.InvariantCulture) + "%");
        }

        console.Write(domains);
        if (assessment.MemoryEvidence is { } memory)
            console.WriteLine(memory.Summary);
        if (assessment.Findings.Count == 0)
        {
            console.MarkupLine("[green]Sin fallos detectados durante esta evaluación.[/]");
            return;
        }

        var findings = new Table().Border(border).Title("[cyan]Hallazgos[/]");
        findings.AddColumn("Prioridad");
        findings.AddColumn("Dominio");
        findings.AddColumn("Hallazgo");
        findings.AddColumn("Riesgo sugerido");
        foreach (var finding in assessment.Findings)
        {
            findings.AddRow(
                finding.Priority.ToString(),
                DomainLabel(finding.Domain),
                Markup.Escape(finding.Summary),
                finding.SuggestedActionRisk.ToString());
        }

        console.Write(findings);
    }

    private void RenderPreflight(StabilityPreflight preflight)
    {
        var table = new Table()
            .Border(plainText ? TableBorder.Ascii : TableBorder.Rounded)
            .Title("[cyan]Preflight de seguridad[/]");
        table.AddColumn("Comprobación");
        table.AddColumn("Estado");
        table.AddColumn("Detalle");
        foreach (var check in preflight.Checks)
        {
            table.AddRow(Markup.Escape(check.Label), check.Status.ToString(), Markup.Escape(check.Detail));
        }

        console.Write(table);
    }

    private void RenderStabilityResult(StabilityTestResult result)
    {
        var table = new Table()
            .Border(plainText ? TableBorder.Ascii : TableBorder.Rounded)
            .Title("[cyan]Resultado de estabilidad[/]");
        table.AddColumn("Etapa");
        table.AddColumn("Estado");
        table.AddColumn("Duración real");
        table.AddColumn("Detalle");
        foreach (var probe in result.Probes)
        {
            table.AddRow(
                Markup.Escape(probe.Label),
                probe.Status.ToString(),
                probe.Duration.ToString("mm\\:ss", CultureInfo.InvariantCulture),
                Markup.Escape(probe.Detail));
        }

        console.Write(table);
        console.MarkupLine($"[bold]Duración total:[/] {result.Duration:mm\\:ss}");
        console.MarkupLine(result.WroteBenchmarkData
            ? "[red]La prueba informó escritura de benchmark.[/]"
            : "[green]No se escribió ningún archivo de benchmark.[/]");

        var metrics = result.Probes
            .SelectMany(probe => probe.Metrics.Select(metric => (probe.Label, metric.Key, metric.Value)))
            .ToArray();
        if (metrics.Length > 0)
        {
            var metricTable = new Table()
                .Border(plainText ? TableBorder.Ascii : TableBorder.Rounded)
                .Title("[cyan]Métricas de rendimiento — no son salud[/]");
            metricTable.AddColumn("Etapa");
            metricTable.AddColumn("Métrica");
            metricTable.AddColumn("Valor");
            foreach (var metric in metrics)
            {
                metricTable.AddRow(
                    Markup.Escape(metric.Label),
                    Markup.Escape(metric.Key),
                    FormatMetric(metric.Key, metric.Value));
            }

            console.Write(metricTable);
        }
    }

    private static string FormatMetric(string key, double value)
    {
        if (key.StartsWith("whea.latest", StringComparison.Ordinal))
        {
            return value == 0
                ? "Ninguno"
                : DateTimeOffset.FromUnixTimeMilliseconds((long)value)
                    .ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        }

        if (key.Contains("bytes", StringComparison.Ordinal))
        {
            return $"{value / (1024d * 1024 * 1024):0.00} GiB";
        }

        if (key.Contains("percent", StringComparison.Ordinal))
        {
            return $"{value:0.0} %";
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string DomainLabel(HealthDomain domain) => domain switch
    {
        HealthDomain.HardwareReliability => "Fiabilidad de hardware",
        HealthDomain.Storage => "Almacenamiento",
        HealthDomain.Memory => "RAM",
        HealthDomain.WindowsIntegrity => "Integridad de Windows",
        HealthDomain.LoadStability => "Estabilidad bajo carga",
        _ => domain.ToString(),
    };

    private static string AvailabilityLabel(AvailabilityState state) => state switch
    {
        AvailabilityState.Available => "[green]Disponible[/]",
        AvailabilityState.Unavailable => "[yellow]No disponible[/]",
        _ => "[grey]Desconocido[/]",
    };

    private static string InstallationLabel(PcManagerInstallationState state) => state switch
    {
        PcManagerInstallationState.Installed => "[green]Instalado[/]",
        PcManagerInstallationState.Absent => "[yellow]Ausente[/]",
        _ => "[grey]Desconocido[/]",
    };

    private static string StorePolicyLabel(StorePolicyState state) => state switch
    {
        StorePolicyState.NotBlockedByKnownPolicy => "[green]Sin bloqueo conocido[/]",
        StorePolicyState.Blocked => "[red]Bloqueada[/]",
        _ => "[grey]Desconocida[/]",
    };

    private static string PcManagerActionLabel(PcManagerProposedAction action) => action switch
    {
        PcManagerProposedAction.LaunchInstalled => "Ofrecer abrir PC Manager",
        PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet => "Proponer instalación oficial mediante WinGet",
        PcManagerProposedAction.OpenOfficialStorePage => "Ofrecer ficha oficial de Microsoft Store",
        _ => "Ninguna acción disponible",
    };

    private void RenderMaintenanceSelection(string menuId)
    {
        var available = menuId switch
        {
            "cleanup" => PhaseFiveUiData.CreateMaintenanceChoices()
                .Where(action => action.Category is "Limpieza" or "Papelera").ToArray(),
            "integrity" => PhaseFiveUiData.CreateMaintenanceChoices()
                .Where(action => action.Category is "Integridad" or "Almacenamiento").ToArray(),
            _ => PhaseFiveUiData.CreateMaintenanceChoices().ToArray(),
        };
        IEnumerable<MaintenanceActionDescriptor> choices = plainText
            ? PromptPlainTextMaintenance(available)
            : PromptAnsiMaintenance(available);

        RenderMaintenancePlan(MaintenancePlanBuilder.BuildPreview(choices.Select(action => action.Id)));
    }

    private MenuOption PromptAnsiMenu() => console.Prompt(
        new SelectionPrompt<MenuOption>()
            .Title("[yellow]Selecciona una opción[/]")
            .PageSize(10)
            .UseConverter(item => item.Label)
            .AddChoices(PhaseFiveUiData.CreateMainMenu()));

    private List<MaintenanceActionDescriptor> PromptAnsiMaintenance(
        IReadOnlyList<MaintenanceActionDescriptor> available) => console.Prompt(
        new MultiSelectionPrompt<MaintenanceActionDescriptor>()
            .Title("[yellow]Selección múltiple — plan de vista previa[/]")
            .NotRequired()
            .PageSize(10)
            .InstructionsText("[grey](Espacio selecciona; Enter confirma la vista previa)[/]")
            .UseConverter(item => $"{item.Label} [{item.Priority}/{item.Risk}]")
            .AddChoices(available));

    private MenuOption PromptPlainTextMenu()
    {
        var options = PhaseFiveUiData.CreateMainMenu();
        while (true)
        {
            WritePlainLine(string.Empty);
            WritePlainLine("Selecciona una opción escribiendo su número:");
            foreach (var option in options)
            {
                WritePlainLine(option.Label);
            }

            WritePlain("Opción: ");
            var input = Console.ReadLine();
            if (input is null)
            {
                return options[^1];
            }

            if (int.TryParse(input, out var number) && number >= 1 && number <= options.Count)
            {
                return options[number - 1];
            }

            WritePlainLine("Entrada no válida. Usa un número del 1 al 10.");
        }
    }

    private MaintenanceActionDescriptor[] PromptPlainTextMaintenance(
        MaintenanceActionDescriptor[] options)
    {
        WritePlainLine(string.Empty);
        WritePlainLine("Selección múltiple — plan de vista previa");
        for (var index = 0; index < options.Length; index++)
        {
            WritePlainLine($"{index + 1}. {options[index].Label} [{options[index].Priority}/{options[index].Risk}]");
        }

        WritePlain("Escribe números separados por coma o Enter para ninguno: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var number) ? number - 1 : -1)
            .Where(index => index >= 0 && index < options.Length)
            .Distinct()
            .Select(index => options[index])
            .ToArray();
    }

    private void WritePlain(string value) => console.Write(new Text(value));

    private void WritePlainLine(string value) => console.Write(new Text(value + Environment.NewLine));

    private void RenderMaintenancePlan(MaintenancePlan plan)
    {
        if (plan.Items.Count == 0)
        {
            console.MarkupLine("[grey]No seleccionaste elementos. No se realizaron cambios.[/]");
            return;
        }

        var table = new Table()
            .Border(plainText ? TableBorder.Ascii : TableBorder.Rounded)
            .Title("[cyan]Plan de mantenimiento — vista previa[/]");
        table.AddColumn("Acción");
        table.AddColumn("Prioridad / riesgo");
        table.AddColumn("Requisitos");
        table.AddColumn("Reversión");
        foreach (var item in plan.Items)
        {
            var action = item.Action;
            var requirements = string.Join(", ", action.Preconditions.Select(precondition => precondition.Id));
            table.AddRow(
                Markup.Escape(action.Label),
                $"{action.Priority} / {action.Risk}",
                Markup.Escape(requirements),
                Markup.Escape(action.Reversal.ToString()));
        }

        console.Write(table);
        console.MarkupLine($"[grey]Esquema {Markup.Escape(plan.SchemaVersion)}; perfil {plan.Profile}; dependencias incluidas automáticamente.[/]");
        console.MarkupLine("[yellow]Vista previa: esta compilación no habilita mutaciones de producción.[/]");
        console.MarkupLine("[green]Resultado: 0 acciones ejecutadas; 0 cambios realizados.[/]");
    }

    private void RenderUnavailable(MenuOption option)
    {
        console.MarkupLine($"[yellow]{Markup.Escape(option.Label)}[/]");
        console.MarkupLine("[grey]Disponible en una fase posterior. Esta vista no ejecuta acciones reales.[/]");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
