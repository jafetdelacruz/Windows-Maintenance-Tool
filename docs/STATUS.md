# Estado actual — 24 de octubre de 2026

## Aceptaciones registradas

- Fases 0–7: aceptadas para el alcance de solo lectura, estabilidad opcional y
  apertura autorizada de Microsoft PC Manager o su ficha oficial de Store.
- La candidata `rc.5` obtuvo 501/501 pruebas automatizadas, 32/32 controles R0
  en Windows Sandbox 11 y 15/15 en Windows 10 22H2 x64. Estas aceptaciones
  corresponden a esa candidata y no se transfieren a otros binarios.
- La prueba de estabilidad en Windows 10 completó diez etapas. No acredita
  hardware en general ni comprueba cancelación.
- La presentación inicial en Windows 11 fue revisada en CMD y Windows Terminal;
  el menú y los acentos se ven correctamente, también en pantalla completa.
- El laboratorio de limpieza con archivos ficticios pasó 100/100 controles,
  repetidos en una VM recreada. Esto valida el laboratorio; la limpieza de
  producción continúa bloqueada.

## Límites actuales

Fase 8 sigue abierta. No se habilitan borrados en equipos reales, reparación de
Windows, optimización, reinicio automático ni instalación automática de PC
Manager. La opción de temporales presenta un inventario. Los registros de
recuperación informan sobre operaciones inciertas y nunca reanudan una limpieza.

La candidata todavía no tiene aceptación final. Quedan puertas interactivas del
paquete exacto, incluidas UAC y teclas de cancelación en Windows 11. La prueba de
cancelación en el segundo equipo con Windows 10 se aplazó por disponibilidad;
su aceptación R0 existente permanece acotada y no se repite sin un cambio
pertinente.

## Próximos pasos

1. Continuar en Windows 11 las comprobaciones interactivas pendientes con el
   paquete exacto de candidata y sus puertas de seguridad.
2. Revisar los resultados antes de decidir si la candidata cumple el alcance
   previsto. Un resultado de laboratorio no habilita limpieza de producción.
3. Si hay cambios que afecten al ejecutable, repetir solo las aceptaciones
   pertinentes para el nuevo paquete.

## Repositorio y evidencia

Este repositorio contiene ahora código fuente, pruebas, scripts de laboratorio
y documentación. No incluye una distribución binaria oficial. Los informes
detallados, capturas, identificadores de sesiones y rutas de usuario se guardan
localmente y no se publican; los resultados anteriores se resumen aquí sin
datos que identifiquen equipos o cuentas.
