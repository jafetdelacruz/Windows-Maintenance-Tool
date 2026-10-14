# ADR-004: Instalación manual de PC Manager

## Estado

Aceptado por el usuario el 8 de octubre de 2026.

## Decisión

WinMaint v1 detecta PC Manager y ofrece abrirlo cuando está instalado. Si falta,
ofrece la ficha oficial de Microsoft Store, ProductId `9PM860492SZD`. El usuario
controla la instalación dentro de Store. WinMaint no ejecuta WinGet ni automatiza
controles de Store o PC Manager.

La instalación automática y el ensayo mutable de PC 2 se retiran del alcance
de v1. La composición del ejecutable bloquea instalaciones incluso en Sandbox
o con el argumento del antiguo laboratorio. Los binarios antiguos de laboratorio
no representan este alcance y no deben usarse.

## Verificación

La puerta de Fase 5 requiere probar la apertura de la ficha oficial y el
lanzamiento instalado. No se reportará una instalación manual como realizada o
verificada por WinMaint. La Fase 6 comienza después de cerrar esa puerta.
