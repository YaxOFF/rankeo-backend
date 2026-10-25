# CLAUDE.md — Rankeo API

Backend .NET 10 + PostgreSQL + SignalR para Rankeo (tier lists en grupo, salas en vivo). El frontend
está en `D:\YaxonFolder\Github\rankeo-frontend` (repo git, React + Vite); va en otro contenedor/origen
que despliega el usuario, y llega a la API por HTTP (`VITE_API_URL`) y por SignalR (`/hubs/sala`).

## Al empezar cada sesión

1. Lee `PLAN.md` completo. Es la única fuente de verdad del plan y del avance.
2. Busca en **Avance** la primera fase sin marcar. Trabaja solo esa fase.
3. Lee la **Bitácora** de la fase anterior: trae decisiones y pendientes.
4. Si necesitas una regla de negocio, léela en el código de la demo: `src/room/roomReducer.ts`,
   `src/room/roomService.ts`, `src/types.ts`, `src/lib/ids.ts`, `src/lib/demo.ts`, y los textos de
   bitácora en `src/pages/RoomPage.tsx` y `src/components/room/TierBoard.tsx` (todo dentro del
   frontend). Para ubicar cosas usa primero `rankeo-frontend/docs/MAP.md`. No inventes reglas que la
   demo no tenga. Si algo no está claro, pregunta al usuario y anota la respuesta en `PLAN.md`.

## Al terminar cada feature importante o fase

1. `dotnet test` en verde (Docker encendido para Testcontainers). Si algo falla, no se marca la fase.
2. Actualiza `PLAN.md`:
   - Marca la fase en **Avance**.
   - Agrega una entrada en **Bitácora**: fecha, qué se hizo, archivos clave, decisiones nuevas, pendientes.
   - Si cambió el diseño (endpoint, tabla, evento del hub, decisión), corrige la sección correspondiente del plan.
3. Commit con mensaje claro (Conventional Commits) y `git push origin main` (si aún no hay remoto, pregunta al usuario si ya lo creó; si no, solo commit local).
4. Avisa al usuario con este mensaje:
   > Fase X terminada y subida a GitHub. Listo para borrar el chat (`/clear`) y empezar la siguiente fase desde cero.

   El objetivo es que cada sesión empiece con contexto limpio. El estado vive en `PLAN.md` y en git,
   no en el chat. No sigas con la siguiente fase en la misma sesión salvo que el usuario lo pida.

## Arquitectura (no cambiar sin preguntar)

- Transporte: **Controllers** (HTTP) y **`SalaHub`** (SignalR). Reglas: **Services** con EF Core directo.
- Estado de sala = documento `jsonb` en `Salas.Estado`. Toda mutación pasa por `Dominio/RoomReducer.cs`
  (port puro de `roomReducer.ts`: si la acción no aplica devuelve la misma instancia → no se guarda,
  no se emite, no se registra).
- Escrituras de una sala serializadas con `SELECT … FOR UPDATE` en una transacción; `Version++` por
  acción aplicada; el hub emite `state`/`activity` **después** del commit.
- Funciones puras (`GenerarCodigo`, `NormalizarCodigo`, `ColorPara`, `TextoBitacora`) en `Services/Calculos.cs`.
- Sin repositorios, sin interfaces de una sola implementación, sin AutoMapper, MediatR ni
  FluentValidation. Services como clases concretas con `AddScoped`.
- Errores: `ReglaException` → 400, `NoEncontradoException` → 404, en un solo `IExceptionHandler`
  (ProblemDetails); en el hub, `HubException`. Mensajes iguales a los de la demo.
- Los DTO y eventos usan los mismos nombres de campo que `src/types.ts` del frontend (+ `version`).
- El cliente nunca manda lo que el servidor calcula (texto y color de bitácora, `by`, `url`).
- Sin login: el código de sala es la llave. La sala `DEMO42` vive solo en el frontend.
- Pruebas de integración con PostgreSQL real (Testcontainers) y `HubConnection` contra el TestServer,
  nunca EF InMemory.

## Restricciones

- Mantener todo simple y proporcional al tamaño del proyecto. No agregar capas, patrones, librerías
  ni abstracciones que el plan no pida.
- No implementar funcionalidades que la demo no tiene (ver "Fuera de alcance" en `PLAN.md`).
- Nada de credenciales en el código: todo sale de `.env` (versionado solo `.env.example`).
- Frontend: es repo git, pero la cuenta `YaxOFF` no tiene permiso de push a `David54s/rankeo-frontend`.
  Antes de la Fase 7 pregunta al usuario a qué rama/remoto subir los cambios.

## Comandos

```bash
cp .env.example .env
docker compose up -d db
dotnet run --project Rankeo.Api
dotnet test
dotnet ef migrations add <Nombre> --project Rankeo.Api --output-dir Data/Migrations
docker compose up --build
```
