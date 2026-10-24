# Integración segura con Microsoft PC Manager — Fase 5

Identificador oficial de Microsoft Store: `9PM860492SZD`.

## Decisión

- Detectar paquetes AppX/MSIX cuyo nombre contenga `PCManager` y cuyo
  `PublisherId` sea el de Microsoft (`8wekyb3d8bbwe`).
- Considerar compatible Windows 10 build `19042` o posterior y Windows 11,
  únicamente x64.
- Consultar por separado WinGet, el paquete de Microsoft Store, políticas
  conocidas de bloqueo y la existencia de una interfaz de red.
- No equiparar una interfaz de red con acceso confirmado a Internet.
- Si un dato requerido es desconocido, bloquear la propuesta.
- Si PC Manager está instalado, ofrecer únicamente abrirlo.
- Si está ausente y Store está disponible, ofrecer la ficha oficial mediante
  `ms-windows-store://pdp/?ProductId=9PM860492SZD`.
- El usuario instala manualmente desde Store, independientemente de WinGet.
- La instalación automática fue retirada del alcance el 8 de octubre de 2026
  ([ADR-004](../architecture/adr-004-pc-manager-manual-installation.md)).
- No descargar instaladores ni paquetes desde direcciones alternativas.
- No automatizar controles internos de PC Manager.

## Comando histórico, retirado de v1

El ejecutable debe resolverse como ruta absoluta a `winget.exe`. Los argumentos
se entregan como elementos separados:

```text
install
--id
9PM860492SZD
--source
msstore
--exact
--interactive
--accept-source-agreements
--accept-package-agreements
```

El ejecutable actualizado no propone ni habilita este comando, tampoco en
Sandbox o mediante el antiguo argumento de PC 2. Los adaptadores y pruebas
históricos se conservan como referencia para una futura validación; no forman
parte del flujo de v1. No usar el binario mutable antiguo de la USB.

## Verificación de apertura

`--pc-manager-store` permite solicitar la ficha oficial aunque PC Manager ya
esté instalado. Comprueba compatibilidad, política, Store y red; exige el token
`pc-manager.store` y solo solicita abrir la aplicación externa. El resultado
no afirma que el usuario haya instalado el paquete ni que la ficha haya cargado.

## Fuentes oficiales

- [Sitio oficial de Microsoft PC Manager](https://pcmanager.microsoft.com/es-mx)
- [Ficha de Microsoft Store](https://apps.microsoft.com/detail/9pm860492szd)
- [WinGet install](https://learn.microsoft.com/en-us/windows/package-manager/winget/install)
- [WinGet sources](https://learn.microsoft.com/en-us/windows/package-manager/winget/source)
- [Get-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage)
- [URI oficiales de Microsoft Store](https://learn.microsoft.com/es-es/windows/apps/develop/launch/launch-store-app)
- [Políticas de acceso a Microsoft Store](https://learn.microsoft.com/en-us/windows/configuration/store/)
