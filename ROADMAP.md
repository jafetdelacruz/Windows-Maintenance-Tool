# Roadmap — WinMaint

Cada fase termina con evidencia y una puerta de revisión. No se avanza dejando
acciones peligrosas sin probar.

## Fase 0 — Especificación y traslado

Estado: en curso.

- [x] Definir alcance Windows 10/11 x64.
- [x] Definir TUI sin interfaz gráfica.
- [x] Separar prioridad, riesgo, salud y rendimiento.
- [x] Diseñar prueba rápida y puntuación con confianza.
- [x] Reservar optimizaciones agresivas para versiones futuras.
- [x] Crear especificación, arquitectura y ADR iniciales.
- [ ] Crear repositorio remoto privado.
- [ ] Clonar en Windows.
- [ ] Verificar entorno de desarrollo en Windows.

Salida: documentación versionada y proyecto disponible en Windows.

## Fase 1 — Esqueleto TUI

- Crear solución .NET 10 y proyectos.
- Añadir Spectre.Console con versión fijada.
- Construir menú, tablero, tablas y selección múltiple.
- Implementar fallback visual sin ANSI.
- Mostrar datos simulados claramente etiquetados.
- Configurar pruebas, analizadores y build reproducible.
- Configurar publicación `win-x64` self-contained single-file.

Puerta:

- Build y tests pasan.
- La TUI funciona en CMD y Windows Terminal.
- No existe ninguna acción real todavía.

## Fase 2 — Diagnóstico de solo lectura

- Sistema operativo, build, uptime y energía.
- CPU, RAM y procesos.
- Discos, volúmenes y contadores de fiabilidad.
- Eventos WHEA y errores relevantes.
- Reinicio pendiente y estado de integridad disponible.
- Fixtures anonimizados para parsers.

Puerta:

- Ejecutar en Windows 10 y Windows 11 x64.
- Confirmar que el modo diagnóstico no modifica el sistema.
- Dato ausente aparece como desconocido.

## Fase 3 — Salud y estabilidad

- Motor de reglas versionado.
- Puntuaciones por dominio.
- Confianza por cobertura.
- Visualización y hallazgos P0-P3.
- Preflight de seguridad.
- Probes de CPU, RAM, disco y enfriamiento.
- Cancelación, timeout y recuperación de sesión.

Puerta:

- Una alerta P0 bloquea carga intensiva.
- Prueba rápida termina en 4–6 minutos en el equipo de referencia.
- No hay escritura de benchmark en disco por defecto.
- Resultados repetidos tienen variación documentada.

## Fase 4 — Mantenimiento seguro

- Inventario y limpieza de temporales.
- Papelera separada.
- Análisis y optimización correcta por tipo de unidad.
- `chkdsk /scan` y propuestas de reparación.
- DISM y SFC.
- Confirmaciones y requisitos de reinicio.
- Verificación posterior a cada acción.

Puerta:

- VM restaurada después de escenarios mutables.
- Cancelación no deja temporales conocidos.
- Toda acción tiene prioridad, riesgo, precondiciones y reporte.

## Fase 5 — Microsoft PC Manager

- Detección AppX/MSIX.
- Detección de WinGet y Store.
- Consentimiento explícito.
- Instalación con `9PM860492SZD` desde `msstore`.
- Fallback a ficha oficial.
- Lanzamiento y reporte.

Puerta:

- Validar instalado, ausente, Store bloqueada, sin red y cancelado.
- No descargar desde fuentes alternativas.

## Fase 6 — Antes/después y reportes

- Snapshot previo y posterior.
- Comparación solo de métricas equivalentes.
- TXT legible y JSON versionado.
- Redacción de seriales.
- Manejo de reportes parciales o interrumpidos.

Puerta:

- Reporte comprensible para una persona no técnica.
- Reporte técnico permite reconstruir decisiones.
- No existe campo cliente/folio.

## Fase 7 — Endurecimiento y publicación

- Matriz Windows 10 22H2/Windows 11 x64.
- CMD y Windows Terminal.
- Pruebas con HDD, SATA SSD y NVMe cuando estén disponibles.
- Consola sin ANSI.
- Revisión de dependencias y binario.
- Hash SHA-256 de la versión.
- Manual de uso, limitaciones y recuperación.

Salida: WinMaint v1 candidata a uso real.

## Futuro — No incluido en v1

- Perfil avanzado con optimizaciones agresivas.
- Prueba extendida de 15–20 minutos.
- Escritura temporal de almacenamiento con consentimiento.
- Integraciones con diagnósticos de fabricantes.
- ARM64.
- Firma de código y canal de actualización.

Cada elemento futuro requiere una decisión de arquitectura y pruebas propias.
