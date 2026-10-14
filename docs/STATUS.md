# Estado actual — 14 de octubre de 2026

## Qué está aprobado

- Fases 0–7: aceptadas para el ejecutable rc.1 y su alcance de solo lectura,
  estabilidad opcional y apertura autorizada de PC Manager/Store.
- rc.2: corrección WHEA comprobada en Windows 11; no hereda la matriz de rc.1.
- rc.3: inventario de temporales R0 aceptado. No elimina archivos del equipo.
- Fase 8 sigue abierta. Laboratorio 0.8.0: 27/27 controles aprobados y repetidos
  después de recrear Sandbox, con el mismo ejecutable. Pruebas automáticas: 169
  después de la integración R0 al CLI.
- Se comprobaron inventario, exclusiones, confirmación, cancelación parcial,
  archivos ocupados/cambiados, enlaces, identidad de ancestros y reportes de
  resultados inciertos. Las mutaciones fueron únicamente sobre archivos ficticios.

## Evidencia preservada

Las evidencias detalladas y sus hashes se conservan localmente, fuera de esta
publicación pública. No se incluyen reportes, capturas, rutas personales ni
identificadores de equipos o sesiones. Este documento es un resumen de avance,
no un paquete de evidencia reproducible ni una distribución del ejecutable.
Los reportes Partial son intencionales: incluyen un fallo inyectado cuyo resultado
queda sin verificar, no se presenta como ausencia de cambios.

## Qué sigue

1. Integración R0 implementada y verificada en desarrollo: opción 5 recibe solo
   inventario agregado, sin rutas ni ejecutor. Una sesión sin diagnóstico completado
   no publica inventario. Dos consultas del menú reutilizan un diagnóstico y
   generan inventarios distintos; JSON/TXT revisados, sin eliminaciones.
2. Empaquetar con nueva identidad y aceptar el flujo del ejecutable resultante.
3. Revisar la futura habilitación y probar el ejecutable final en Windows 10/11.
   La aceptación del laboratorio no acepta automáticamente un binario nuevo.

La validación final en Windows 10 está aplazada por disponibilidad del entorno.
No se elimina esa puerta de aceptación ni se cierra Fase 8. No se habilitan
reinicios automáticos, reparación, optimización ni limpieza real.

Los binarios publicados y sus paquetes de evidencia son inmutables. El código
de desarrollo posterior a rc.3 todavía no es una nueva candidata publicada.

## Alcance del repositorio público

Esta actualización publica solo documentación revisada. El código, la
configuración de desarrollo, los binarios y las pruebas permanecen locales.
Las candidatas mencionadas corresponden a validaciones locales; no se ofrecen
como descargas públicas en esta actualización.
