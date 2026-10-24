# WinMaint

WinMaint es una herramienta de consola para diagnóstico y mantenimiento guiado
de equipos Windows 10/11 x64. La interfaz usa CMD o Windows Terminal.

## Estado

Consulta [docs/STATUS.md](docs/STATUS.md) para el avance y los siguientes pasos.
Este repositorio incluye la implementación, las pruebas y los documentos de
arquitectura. No contiene una distribución binaria oficial. Las validaciones
descritas son específicas de los paquetes y entornos indicados; no equivalen a
una certificación universal del hardware ni de cada instalación.

La limpieza de producción permanece bloqueada. La opción de temporales muestra
un inventario; no elimina archivos. Reparación y optimización no se ejecutan.
PC Manager se abre o se ofrece en Microsoft Store para instalación manual.

## Compilar

Requisitos: Windows x64 y .NET SDK 10. Restaura dependencias fijadas y compila:

```powershell
dotnet restore .\WinMaint.sln --locked-mode
dotnet build .\WinMaint.sln --no-restore
```

Para ejecutar la CLI durante el desarrollo:

```powershell
dotnet run --project .\src\WinMaint.Cli\WinMaint.Cli.csproj
```

Consulta [docs/MANUAL.md](docs/MANUAL.md) para el flujo de usuario y
[AGENTS.md](AGENTS.md) para las reglas de contribución. No publiques reportes
de ejecución ni datos identificables de equipos.

## Límites de seguridad

- Las funciones de limpieza no modifican archivos de producción.
- Ningún dato ausente se interpreta como estado saludable.
- No se desactivan Defender, Windows Update ni servicios esenciales.
- No se aplican optimizaciones agresivas ni cambios genéricos del registro.
- Los seriales y otros identificadores sensibles se ocultan en reportes.
- Las pruebas mutables se limitan a laboratorios desechables con archivos
  ficticios.

## Documentos principales

- [SPEC.md](SPEC.md)
- [ARCHITECTURE.md](ARCHITECTURE.md)
- [ROADMAP.md](ROADMAP.md)
- [Decisiones de arquitectura](docs/architecture)
- [Aviso de soporte de Windows](docs/diagnostics/windows-support-notice.md)
