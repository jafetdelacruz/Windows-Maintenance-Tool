# Roadmap — WinMaint

Cada fase termina con evidencia y una puerta de revisión. No se avanza dejando
acciones peligrosas sin probar.

Este roadmap resume desarrollo y validaciones locales. El repositorio público
solo publica documentación; las evidencias detalladas no se incluyen por
privacidad. Estado actual: [docs/STATUS.md](docs/STATUS.md). Los párrafos de
avance dentro de cada fase se conservan en orden cronológico, no son todos
pendientes vigentes.

## Fase 0 — Especificación y traslado

Estado: completada en Windows 11 x64 el 4 de septiembre de 2026.

- [x] Definir alcance Windows 10/11 x64.
- [x] Definir TUI sin interfaz gráfica.
- [x] Separar prioridad, riesgo, salud y rendimiento.
- [x] Diseñar prueba rápida y puntuación con confianza.
- [x] Reservar optimizaciones agresivas para versiones futuras.
- [x] Crear especificación, arquitectura y ADR iniciales.
- [x] Crear repositorio remoto privado.
- Visibilidad actual: repositorio público con publicación limitada a documentación.
- [x] Clonar en Windows.
- [x] Verificar entorno de desarrollo en Windows.

Salida: documentación versionada y proyecto disponible en Windows.

## Fase 1 — Esqueleto TUI

Estado: completada en Windows 11 x64 el 4 de septiembre de 2026.

- [x] Crear solución .NET 10 y proyectos.
- [x] Añadir Spectre.Console con versión fijada.
- [x] Construir menú, tablero, tablas y selección múltiple.
- [x] Implementar fallback visual sin ANSI.
- [x] Mostrar datos simulados claramente etiquetados.
- [x] Configurar pruebas, analizadores y build reproducible.
- [x] Configurar publicación `win-x64` self-contained single-file.

Puerta:

- [x] Build y tests pasan.
- [x] La TUI funciona en CMD y Windows Terminal.
- [x] No existe ninguna acción real todavía.

Evidencia detallada de Fase 1 conservada localmente.

## Fase 2 — Diagnóstico de solo lectura

Estado: completada en Windows 10 22H2 x64 y Windows 11 x64 el 4 de septiembre
de 2026.

- [x] Sistema operativo, build, uptime y energía.
- [x] CPU, RAM y procesos.
- [x] Discos, volúmenes y contadores de fiabilidad.
- [x] Eventos WHEA y errores relevantes.
- [x] Reinicio pendiente y estado de integridad disponible; se muestra
  `Desconocido` sin ejecutar verificadores que escriben logs.
- [x] Fixtures anonimizados para parsers.

Puerta:

- [x] Ejecutar en Windows 10 y Windows 11 x64.
  - [x] Windows 10 22H2 x64 real.
  - [x] Windows 11 x64 real.
- [x] Confirmar que el modo diagnóstico no modifica el sistema.
- [x] Dato ausente aparece como desconocido.

Evidencia detallada de Fase 2 conservada localmente.
La Fase 3 está completada.

## Fase 3 — Salud y estabilidad

Estado: completada en Windows 11 x64 el 4 de septiembre de 2026.

- [x] Motor de reglas versionado.
- [x] Puntuaciones por dominio.
- [x] Confianza por cobertura.
- [x] Visualización y hallazgos P0-P3.
- [x] Preflight de seguridad.
- [x] Probes de CPU, RAM, disco y enfriamiento.
- [x] Cancelación, timeout y recuperación de sesión.

Puerta:

- [x] Una alerta P0 bloquea carga intensiva.
- [x] Prueba rápida termina en 4–6 minutos en el equipo de referencia.
- [x] No hay escritura de benchmark en disco por defecto.
- [x] Resultados repetidos tienen variación documentada.

Evidencia detallada de Fase 3 conservada localmente.
La Fase 4 está completada.

## Fase 4 — Mantenimiento seguro

Estado: completada en Windows 11 x64 el 9 de septiembre de 2026. El adaptador
de temporales se validó en Windows Sandbox; las mutaciones de producción y los
adaptadores todavía no validados permanecen bloqueados.

- [x] Inventario y limpieza de temporales.
- [x] Papelera separada en el catálogo y el plan.
- [x] Análisis y optimización correcta por tipo de unidad definidos.
- [x] `chkdsk /scan` y límites de reparación definidos.
- [x] Orden y condiciones de DISM y SFC definidos.
- [x] Confirmaciones y requisitos de reinicio modelados.
- [x] Verificación posterior exigida por el contrato de reporte.

Puerta:

- [x] Laboratorio descartado y recreado después de escenarios mutables.
- [x] Cancelación no deja temporales conocidos.
- [x] Toda acción tiene prioridad, riesgo, precondiciones y reporte.

Evidencia detallada de Fase 4 conservada localmente.
La Fase 5 está completada con instalación manual desde Store.

## Fase 5 — Microsoft PC Manager

Estado: completada el 8 de octubre de 2026 con alcance manual. La detección y el
planificador son R0. Desde el 8 de octubre de 2026 la instalación es manual
desde Store; WinMaint no ejecuta WinGet. La apertura de Store y el lanzamiento
requieren autorización exacta.

- [x] Detección AppX/MSIX.
- [x] Detección de WinGet y Store.
- [x] Consentimiento explícito.
- [x] Instalación manual desde la ficha oficial `9PM860492SZD`, sin ejecutar
  WinGet. Alcance aprobado por el usuario el 8 de octubre de 2026.
- [x] Apertura de ficha oficial implementada.
- [x] Lanzamiento y reporte.

Puerta:

- [x] Validar instalado, ausente, Store bloqueada, sin red y cancelado.
- [x] Validar el lanzamiento con autorización exacta, sin automatizar controles
  internos de PC Manager.
- [x] Confirmar la apertura real de la ficha oficial en Microsoft Store.
- [x] No existe ninguna ruta de descarga desde fuentes alternativas.

Evidencia detallada de Fase 5 conservada localmente.

## Fase 6 — Antes/después y reportes

Estado: completada el 9 de octubre de 2026. El usuario aprobó el resumen breve
como comprensible; el JSON conserva la evidencia técnica completa.

- [x] Snapshot previo y posterior.
- [x] Comparación solo de métricas equivalentes.
- [x] TXT legible y JSON versionado.
- [x] Redacción de seriales.
- [x] Manejo de reportes parciales o interrumpidos.

Puerta:

- [x] Reporte comprensible para una persona no técnica: aprobado por el usuario.
- [x] Reporte técnico permite reconstruir decisiones.
- [x] No existe campo cliente/folio.

Evidencia detallada de Fase 6 conservada localmente.

## Fase 7 — Endurecimiento y publicación

Estado: completada el 9 de octubre de 2026 para `v1.0.0-rc.1`, con evidencia
del mismo ejecutable en Windows 10 22H2 x64 y Windows 11 x64. Las mutaciones
de producción siguen bloqueadas; la candidata no tiene firma digital.

- [x] Matriz Windows 10 22H2/Windows 11 x64 con la candidata actual.
- [x] CMD y Windows Terminal con la candidata actual: menú y acentos confirmados.
- [x] Diagnóstico de solo lectura con HDD, SATA SSD y NVMe disponibles.
- [x] Consola sin ANSI: ejecutable exacto verificado en Windows 11 x64.
- [x] Revisión de dependencias y binario.
- [x] Hash SHA-256 de la versión y reproducibilidad.
- [x] Manual de uso, limitaciones y recuperación.

Evidencia detallada de Fase 7 conservada localmente.

Salida: WinMaint v1 candidata a uso real.

## Corrección previa a Fase 8 — Clasificación WHEA

Estado: completada con el diagnóstico de rc.2 en Windows 11 y lectura de su
reporte. Se conserva rc.1 y su evidencia de Fase 7 sin modificaciones.

- [x] Separar WHEA informativos, errores y registros desconocidos.
- [x] Versionar reglas y añadir pruebas de regresión.
- [x] Diagnóstico real R0: 4 informativos, 0 errores, sin penalización WHEA.
- [x] Confirmar diagnóstico del ejecutable rc.2 elevado y leer su reporte.

Evidencia detallada de la corrección WHEA conservada localmente.

## Fase 8 — Limpieza segura de temporales

Estado: iniciada el 9 de octubre tras aceptar la corrección WHEA. Inventario R0
implementado y aceptado en rc.3 el 10 de octubre; no hay borrado habilitado.
Prototipo de borrado separado preparado en Sandbox; cancelación previa
aprobada por confirmación del operador, al igual que los ocho ensayos de la
fixture mutable y su repetición tras recrear Sandbox. El arnés corregido 0.2.1
también pasó 15/15 y repetición, con captura preservada. Recorrido real e
integración siguen pendientes; las puertas combinadas no se marcan como
aprobadas para un ejecutor de producción.
Lote automático 0.3.0 y exportación aprobados mediante lectura de los dos JSON
del mismo lote: cancelación sin eliminación y 15/15 de fixture; evidencia
preservada fuera del inbox. No sustituye integración del flujo de producción.
13 de octubre: inventario estructurado y reportes 1.1 conectados a opción 5;
coordinador de autorización/cancelación probado con runners falsos. 146 tests
aprobados y flujo real de inventario sin borrado verificado. Falta conectar y
ensayar el adaptador nativo con el recorrido real; producción sigue bloqueada.
Preparado arnés automático 0.4.0 con proveedor real sobre fixture plana,
coordinador, puente nativo y reportes de producto. Diez comprobaciones de
integración pendientes de ejecutar en Sandbox; no sustituyen recorrido recursivo.
Integración plana 0.4.0 aprobada 10/10 mediante lectura de manifest y reportes
reales del producto. Preparado lote 0.5.0 de 16 comprobaciones con carpetas
anidadas, áreas protegidas y verificación posterior; aceptación pendiente.
Lote 0.5.0 aprobado 16/16 mediante manifest y JSON/TXT leídos y preservados.
Pendiente repetición del mismo binario después de descartar y recrear Sandbox,
revisión de conexión a producción y matriz del ejecutable final.
Repetición 0.5.0 tras recreación aprobada 16/16, con JSON/TXT preservados.
Preparado 0.6.0 para cancelación e interrupción integradas después de una
eliminación ficticia real; 20 comprobaciones pendientes de Sandbox. No se
habilita producción ni se hereda la matriz de aceptación de rc.1.
14 de octubre: 0.6.0 aprobado 20/20 leyendo manifest y JSON/TXT del producto;
cancelación conserva resultados y fallo inyectado queda Unverified. Repetición
tras recrear pendiente; producción continúa bloqueada.
Repetición 0.6.0 aprobada 20/20 con evidencia preservada. Núcleo nativo trasladado
a Infrastructure.Win, deshabilitado por defecto y sin llamador en la CLI;
laboratorio 0.7.0 preparado para los mismos 20 casos con código compartido.
150 tests aprobados; aceptación nativa del paquete recompilado pendiente.
Núcleo compartido 0.7.0 aprobado 20/20 con JSON/TXT leídos y preservados.
Pendiente recreación y repetición del mismo binario, composición de raíz estricta
del usuario para el producto y matriz del ejecutable final. Producción bloqueada.
Repetición del núcleo compartido 0.7.0 aprobada 20/20 tras recreación, con
reportes preservados. No hace falta repetir este mismo lote sin cambios;
restan composición de raíz real y aceptación del binario final antes de habilitar.

- [x] Definir raíces permitidas, antigüedad mínima, riesgo y exclusiones.
- [x] Inventario previo sin borrar: opción 5 de rc.3 ejecutada y reporte leído.
  Los candidatos no autorizan borrado.
- [x] Inventario en Sandbox: siete ensayos de seguridad y repetición tras
  recrear la sesión, sin conservar la marca anterior ni borrar archivos.
- [ ] Bloquear enlaces/reparse points, rutas ajenas y archivos recientes/en uso.
- [ ] Confirmación explícita, cancelación y reporte de resultados reales.
- [ ] Validar en Windows Sandbox recuperable y recrearlo tras las pruebas.
- [ ] Validar que cancelar no borra, que los archivos excluidos sobreviven y
  que la verificación posterior explica lo eliminado y lo omitido.
- [ ] Revisar evidencia antes de habilitar cualquier limpieza de producción.

Papelera, cachés de aplicaciones, OneDrive, Descargas, documentos, Windows
Update, registro, reparación y optimización quedan fuera de esta fase inicial.
No se borrará ningún archivo de este equipo durante la preparación.

Evidencia detallada de Fase 8 conservada localmente.

14 de octubre: preparación estricta de raíz/candidatos implementada sin conectar
ejecución a la CLI. 163 tests pasan; nuevo lote Sandbox 0.8.0 con 27 controles
preparado, todavía pendiente de ejecutar. PC 2 Windows 10 se aplaza mientras está
ocupada; su validación del ejecutable final continúa siendo obligatoria.
No se habilita limpieza de producción ni se cierra Fase 8.

Preparación estricta 0.8.0 aprobada 27/27 mediante lectura y validación de
manifest y reportes del producto; evidencia preservada. Pendiente repetir el
mismo binario en Sandbox recreado. Producción continúa bloqueada.

Recreación de preparación estricta 0.8.0 aprobada 27/27 con el mismo binario y
nuevo lote; JSON/TXT revisados y evidencia preservada. No hace falta repetir
este paquete sin cambios. Siguiente trabajo: integrar el componente en el
flujo CLI manteniendo el bloqueo de producción, revisar y verificar ese flujo.
La aceptación del ejecutable final Windows 10/11 sigue pendiente.

Integración R0 al CLI implementada: opción 5 recibe solo agregados de la
preparación nativa estricta, conserva identidad/fecha y exige diagnóstico previo.
169 tests pasan (7 Domain, 80 Application, 82 Windows). Flujo de desarrollo
5 → 5 → 10 ejecutado en Windows 11; JSON/TXT leídos: un diagnóstico, dos
inventarios distintos y ninguna eliminación. Paquetes rc.3 y lab 0.8.0 intactos.
Queda empaquetar con nueva identidad y aceptar el flujo del ejecutable; no se
habilita producción ni se cierra la fase. Estado resumido: `docs/STATUS.md`.

## Futuro — No incluido en v1

- Instalación automática de PC Manager mediante WinGet, sujeta a validación
  mutable en laboratorio recuperable.
- Perfil avanzado con optimizaciones agresivas.
- Prueba extendida de 15–20 minutos.
- Escritura temporal de almacenamiento con consentimiento.
- Integraciones con diagnósticos de fabricantes.
- ARM64.
- Firma de código y canal de actualización.

Cada elemento futuro requiere una decisión de arquitectura y pruebas propias.
