# WinMaint

WinMaint es una utilidad TUI portátil para diagnosticar y mantener equipos con
Windows 10 y Windows 11 x64. Su interfaz vive en CMD o Windows Terminal y usa
menús, tablas, colores, selección múltiple y progreso en vivo.

## Estado

El proyecto está en especificación. Todavía no ejecuta diagnósticos ni modifica
Windows.

## Objetivos de la versión 1

- Diagnosticar antes de modificar.
- Presentar hallazgos P0-P3 y riesgo por separado.
- Evaluar RAM, almacenamiento, integridad de Windows y estabilidad básica.
- Mostrar una puntuación de salud transparente y su nivel de confianza.
- Comparar métricas antes y después del mantenimiento.
- Ejecutar limpieza y reparación con confirmaciones y límites seguros.
- Detectar, instalar y abrir Microsoft PC Manager con autorización explícita.
- Generar un único ejecutable self-contained para Windows x64.

## Base técnica

- C#.
- .NET 10 LTS.
- Spectre.Console para la TUI.
- Arquitectura monolítica modular.
- Proveedores nativos de Windows, PowerShell y procesos controlados.
- Pruebas unitarias fuera de Windows mediante adaptadores simulados.
- Pruebas de integración y aceptación únicamente en Windows 10/11 x64.

Se eligió .NET 10 porque su soporte LTS llega hasta noviembre de 2028. .NET 8
termina soporte en noviembre de 2026.

## Lectura obligatoria para continuar

1. [SPEC.md](SPEC.md)
2. [ARCHITECTURE.md](ARCHITECTURE.md)
3. [ROADMAP.md](ROADMAP.md)
4. [docs/architecture](docs/architecture)

## Restricciones importantes

- No se desactivan Defender, Windows Update ni servicios esenciales.
- No se aplican ajustes genéricos o dudosos del registro.
- No se inventan porcentajes de salud ni progreso.
- Un dato no disponible reduce la confianza; no se interpreta como sano.
- Una alerta P0 bloquea pruebas intensivas y prioriza respaldo de datos.
- La versión 1 no incluye optimizaciones agresivas.
- No se almacenan campos de cliente o folio.
- Números de serie y otros identificadores sensibles se ocultan por defecto.

## Desarrollo entre equipos

La documentación puede editarse en macOS. Las APIs, permisos, comandos y el
ejecutable deben verificarse en Windows real o en máquinas virtuales de Windows
10 y Windows 11 x64.

## Fuentes principales

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Spectre.Console](https://spectreconsole.net/)
- [Microsoft PC Manager](https://pcmanager.microsoft.com/en-us)
- [WinGet install](https://learn.microsoft.com/en-us/windows/package-manager/winget/install)
- [Get-StorageReliabilityCounter](https://learn.microsoft.com/en-us/powershell/module/storage/get-storagereliabilitycounter)
- [WHEA hardware error events](https://learn.microsoft.com/en-us/windows-hardware/drivers/whea/whea-hardware-error-events)
- [DISM and SFC](https://support.microsoft.com/en-us/windows/using-system-file-checker-in-windows-365e0031-36b1-6031-f804-8fd86e0ef4ca)
- [Optimize-Volume](https://learn.microsoft.com/en-us/powershell/module/storage/optimize-volume)
