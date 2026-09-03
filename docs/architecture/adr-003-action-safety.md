# ADR-003: Modelo de seguridad de acciones

## Estado

Aceptado.

## Contexto

El proyecto automatiza mantenimiento con privilegios administrativos y debe
permitir acciones más agresivas en una versión futura sin volver insegura v1.

## Decisión

Cada acción declara prioridad, riesgo, precondiciones, recursos, privilegios,
reinicio, reversión y perfil. V1 solo expone el perfil seguro.

## Razones

- Prioridad no equivale a autorización.
- El planificador puede evitar operaciones conflictivas.
- Los perfiles futuros se añaden sobre contratos explícitos.
- Las confirmaciones y reportes pueden generarse desde los mismos metadatos.

## Consecuencias

- Añadir una acción requiere más documentación y pruebas.
- Las mutaciones se ejecutan secuencialmente por defecto.
- El modo avanzado requerirá un ADR y validación separados.

## Revisar cuando

- Se implemente el primer perfil avanzado.
- Se requieran acciones paralelas mutables.
