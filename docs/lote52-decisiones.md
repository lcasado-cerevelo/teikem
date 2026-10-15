# Lote 52 — Pantalla «Reglas de aviso», campanita y Kemi (P2.1 etapa b, 2026-10-14)

Cuarto lote de P2. Hecho en el contexto principal, sin agentes. **Va en el cherry-pick a Depot-Implementation** (junto con 50, 51 y 53).

## Qué se construyó
- **Sistema → Reglas de aviso** (`/system/notifications`, `notifications.manage`): lista de reglas con interruptor, modal de alta y edición (evento fijo al editar, destinatarios rol/usuario/correo/teléfono, canales con plantilla es/en y fichas de {variables}, canales sin configurar apagados con su explicación, «Enviar prueba» por canal) y pestaña **Registro** (filtros evento/canal/estado, paginado del servidor, exportación completa, «Disparar aviso de prueba»).
- **Buscador de personas** (`UserPicker`): escribe y lista **nombre y correo** (flechas + Enter, accesible); lo reutilizará «Enviar un mensaje a…» (Lote 53).
- **Campanita** en la cabecera: contador (99+), lista de los últimos avisos, clic = marcar leído y abrir el enlace interno, «Marcar todo como leído», silenciar eventos opcionales; el contador se refresca cada 30 s.
- **Kemi, capa de presentación** (decisión de Luis): mascota flotante que parpadea con el globo «Te llegó un aviso → Ver» cuando llega uno (y saluda una vez por sesión), panel de **opciones en botones** (Ver mis avisos / Dormir a Kemi), **dormida** se esconde en la cabecera junto a la campanita y un clic la despierta (recordado por usuario en el navegador); respeta «reducir movimiento». **No reemplaza la campanita.**
- Nav: «Reglas de aviso» (orden 60 en Sistema; Trabajos programados 70, Seguridad y auditoría 80, Ajustes 90).
- Manual: capítulo de pantalla `frontend/l52-avisos-y-kemi.md` (capturas `l52-*`) y FAQ «Lote 52».

## Pruebas
- vitest: reglas puras (cuándo avisa Kemi, enlaces, formulario, buscador), campanita/Kemi (contador, lista, marcar leído, globo en la primera lectura y cuando sube el contador, dormir/despertar, silenciar), pantalla de reglas (lista, interruptor, alta con buscador, canales sin configurar, editar + prueba, error del servidor, registro, disparar prueba): 33 nuevas. **Suite completa vitest: 226 archivos, 2821 pruebas en verde.** `tsc -b` limpio.
- Playwright `lote52-avisos.spec.ts` (escritorio y móvil 360 px) contra el API real: regla → aviso de prueba por la cola real → registro → globo de Kemi → campanita → marcar leído → dormir/despertar.

## Corrección de paso
- Al correr la suite completa apareció que el Lote 50 había dejado rota `navigation.test.ts` (43 ítems, orden por posición × 10): se arregló en un commit aparte (`1882235`) para que viaje con el 50 en el cherry-pick.

## Decisiones por omisión (a revisar con Luis)
1. Kemi guarda «dormida» **por usuario en el navegador** (localStorage), no en el servidor.
2. El globo se esconde solo a los **15 s**; el saludo inicial es **una vez por sesión de navegador**.
3. La campanita lista los **últimos 20** avisos (sin «ver todos» todavía).
4. Las opciones de Kemi son solo **Ver mis avisos** y **Dormir a Kemi**; «Enviar un mensaje a…» llega en el Lote 53.
5. Al editar una regla se manda la lista **completa** de destinatarios y los cuatro canales (reemplaza).
