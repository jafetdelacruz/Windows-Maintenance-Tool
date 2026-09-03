# ADR-002: Salud, rendimiento y confianza separados

## Estado

Aceptado.

## Contexto

El usuario necesita una puntuación visual para explicar el estado del equipo.
Un benchmark por sí solo confunde antigüedad con fallo y puede dar una falsa
garantía.

## Decisión

Calcular salud por evidencia en cinco dominios, reportar rendimiento aparte y
acompañar la puntuación con cobertura/confianza y hallazgos P0-P3.

## Razones

- Un equipo lento puede estar sano.
- Sensores y contadores no siempre están disponibles.
- La confianza hace visible la calidad del diagnóstico.
- Los límites P0/P1 impiden que un promedio oculte un fallo grave.

## Consecuencias

- Las reglas necesitan versión y pruebas.
- La puntuación requiere explicación en el reporte.
- No puede presentarse como certificación de hardware.

## Revisar cuando

- Exista una base de referencia validada con suficientes equipos.
- Se añadan diagnósticos de fabricantes.
