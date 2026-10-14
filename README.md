# WinMaint

WinMaint es una utilidad TUI portátil para diagnosticar y mantener equipos con
Windows 10 y Windows 11 x64. Su interfaz vive en CMD o Windows Terminal y usa
menús, tablas, colores, selección múltiple y progreso en vivo.

## Estado

Resumen vigente y siguiente paso: [docs/STATUS.md](docs/STATUS.md).

Este repositorio público contiene documentación del proyecto. La implementación,
los ejecutables y las evidencias de laboratorio se conservan localmente y no
forman parte de esta publicación. Los avances descritos no equivalen a una
versión descargable ni a una compilación reproducible desde este repositorio.

Las Fases 0–7 están completadas para `v1.0.0-rc.1`, candidata validada con
el alcance documentado. Están disponibles diagnóstico, estabilidad opcional, apertura de
PC Manager/Store y reportes breves TXT con evidencia técnica JSON.

Limpieza, reparación y optimización permanecen como vistas previas: no se
habilitan mutaciones de producción. PC Manager se instala manualmente desde
Store. La candidata final solicita administrador al abrirse, sin que eso
autorice mantenimiento automático. La candidata exacta pasó las pruebas de
aceptación en Windows 10 22H2/Windows 11 x64 y la revisión visual en CMD y
Windows Terminal. No tiene firma digital ni se presenta como versión estable.

Guía de uso: [docs/MANUAL.md](docs/MANUAL.md).

Corrección posterior: rc.2 separa WHEA informativos de errores (reglas 1.0.1).
Corrección verificada con diagnóstico real elevado de rc.2 en Windows 11.
La matriz aprobada de Fase 7 sigue identificando únicamente rc.1.
La rc.3 inicia Fase 8: opción 5 inventaría temporales sin borrar; inventario
aceptado con reporte real el 10 de octubre. No hay mutaciones habilitadas.

14 de octubre: Fase 8 continúa abierta. El laboratorio 0.8.0 pasó 27/27 y
repetición en Sandbox recreado, con evidencia preservada; 163 tests automáticos
aprobados. Sigue integrar la preparación estricta al CLI sin habilitar borrado.
Windows 10 del ejecutable final está pendiente; PC 2 se aplaza por disponibilidad.

Integración posterior en desarrollo: opción 5 usa preparación nativa estricta
a través de un contrato solo de lectura; 169 tests aprobados. Flujo real del
menú y reportes verificado sin borrado. No cambia ni reemplaza el paquete rc.3.

## Desarrollo y publicación

El desarrollo local usa .NET 10 y pruebas automatizadas, además de ensayos
aislados en Windows. No se publican instrucciones de compilación que dependan
de código ausente en este repositorio. Una futura publicación de código o
ejecutables requiere revisión y autorización separadas.

No se incluyen reportes reales, capturas, identificadores de equipos, rutas de
usuario ni archivos de salida de las pruebas. Se publica únicamente el resumen
técnico revisado de alcance, decisiones y aceptación.

## Objetivos de la versión 1

- Diagnosticar antes de modificar.
- Presentar hallazgos P0-P3 y riesgo por separado.
- Evaluar RAM, almacenamiento, integridad de Windows y estabilidad básica.
- Mostrar una puntuación de salud transparente y su nivel de confianza.
- Comparar métricas antes y después del mantenimiento.
- Mostrar planes de limpieza y reparación; ejecutar solo adaptadores validados
  cuando una futura validación permita habilitarlos en producción.
- Detectar y abrir PC Manager o su ficha oficial con autorización explícita.
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

El [estado resumido](docs/STATUS.md) distingue avances aceptados y pendientes.
Las evidencias detalladas se conservan localmente; no acompañan esta publicación.

## Fuentes principales

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Spectre.Console](https://spectreconsole.net/)
- [Microsoft PC Manager](https://pcmanager.microsoft.com/en-us)
- [WinGet install](https://learn.microsoft.com/en-us/windows/package-manager/winget/install)
- [Get-StorageReliabilityCounter](https://learn.microsoft.com/en-us/powershell/module/storage/get-storagereliabilitycounter)
- [WHEA hardware error events](https://learn.microsoft.com/en-us/windows-hardware/drivers/whea/whea-hardware-error-events)
- [DISM and SFC](https://support.microsoft.com/en-us/windows/using-system-file-checker-in-windows-365e0031-36b1-6031-f804-8fd86e0ef4ca)
- [Optimize-Volume](https://learn.microsoft.com/en-us/powershell/module/storage/optimize-volume)
