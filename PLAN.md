# Rankeo API — Plan de implementación

Backend .NET 10 + PostgreSQL + SignalR para Rankeo (tier lists en grupo). Sustituye a `LocalRoomService`
(salas en `localStorage`) por salas compartidas en vivo: todos los que abren el mismo código ven los
mismos cambios al momento.

> **Estado actual:** ver [Avance](#avance) al final. Trabajar siempre la siguiente fase sin marcar.

## Dónde está cada cosa

| Qué | Ruta |
|---|---|
| Backend (este repo) | `D:\YaxonFolder\Github\rankeo-backend` → remoto `origin` = https://github.com/YaxOFF/rankeo-backend |
| Frontend | `D:\YaxonFolder\Github\rankeo-frontend` (repo git; push al remoto `yaxoff` = https://github.com/YaxOFF/rankeo-frontend- ; `origin` = David54s, sin permiso de push) |
| Docs del frontend (leer primero) | `rankeo-frontend/docs/MAP.md`, `docs/architecture/state-management.md`, `docs/architecture/data-layer.md` |
| Lógica de negocio a portar | `src/room/roomReducer.ts` (todas las mutaciones), `src/room/roomService.ts` (bitácora, códigos, demo) |
| Modelo / contrato | `src/types.ts` (`Room`, `Tier`, `Item`, `RoomAction`, `ActivityEntry`, `POOL`) |
| Utilidades a portar | `src/lib/ids.ts` (`generateCode`, `normalizeCode`, `colorFor`), `src/lib/demo.ts` (`newRoom`, `MAX_TIERS`, `TIER_COLORS`) |
| Textos de bitácora (hoy en el cliente) | `src/pages/RoomPage.tsx` (`addImages`, `addTier`), `src/components/room/TierBoard.tsx` (`onDragEnd`, `onKeyDown`, `onClear`, `onDelete`, `placeName`) |
| Vistas que consumirán la API | `src/room/useRoom.ts`, `src/components/home/Hero.tsx`, `src/components/home/JoinModal.tsx`, `src/pages/RoomPage.tsx` |
| Pruebas existentes | `src/room/roomReducer.test.ts` (9 casos Vitest — se portan 1:1 a xUnit) |

El frontend va en **otro contenedor/origen** (lo despliega el usuario); habla con la API por HTTP
(`VITE_API_URL`) y por SignalR (`/hubs/sala`).

## Primeros pasos

Requisitos verificados: .NET SDK 10.0.401 (también 8.0.419), Docker 29.8.2, Compose v5.6.0.

```bash
cp .env.example .env
docker compose up -d db
dotnet run --project Rankeo.Api
dotnet test                       # Docker encendido (Testcontainers)
docker compose up --build         # stack completo
```

## Contexto

La demo ya está diseñada para esto: toda mutación es una `RoomAction` aplicada por un reducer puro, y
la UI solo habla con la interfaz `RoomService` (`exists/create/load/dispatch/subscribe`). El README
del frontend lo dice: "implementa `RoomService` (por ejemplo con WebSockets)". Hoy la portada promete
"salas en tiempo real" pero dos navegadores (ni dos pestañas) no comparten sala.

Decisiones ya tomadas con el usuario:
- **Grupos:** grupo = sala. La gente entra con el código y un apodo; **sin cuentas ni login** (igual que la demo: "sin cuentas ni instalaciones").
- **Tiempo real:** **SignalR** (viene con ASP.NET Core; usa WebSockets por debajo; grupos por sala nativos).
- **Imágenes:** archivos en **disco (volumen Docker)**, servidos por la API.
- **Frontend:** otro contenedor/origen → CORS con orígenes explícitos.

## 1. Análisis funcional (requisitos confirmados en la demo)

| Módulo | Funcionalidad | Reglas (fuente) |
|---|---|---|
| Salas | Crear con título | Título vacío → `'Mi Tier List'`; máx 80 (`Hero` `maxLength`); filas iniciales S/A/B/C/D con `TIER_COLORS[0..4]` (`newRoom`) |
| Salas | Código | 6 chars de `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, único, nunca `DEMO42` (`generateCode`, `LocalRoomService.create`) |
| Salas | Unirse / existe | Código normalizado: `trim`, mayúsculas, solo `A-Z0-9` (`normalizeCode`); `JoinModal` exige 6 chars; inexistente → "No existe ninguna sala con el código X." |
| Imágenes | Agregar | Llega al final del banco (`item:add`); nombre = archivo sin extensión, máx 30, o `'Imagen'`; `by` = apodo; cliente recorta a 256px webp (`shrink`) |
| Imágenes | Mover | `item:move`: quitar de su lista, insertar en `target` en `index` acotado a `[0, len]`, default final; target o item inexistente → sin cambio |
| Imágenes | Eliminar | `item:delete`: quitar de su lista y del índice `items` |
| Filas | Añadir | `tier:add`: etiqueta `'Nuevo'`, color dado; máx `MAX_TIERS = 15` → "Máximo 15 filas." |
| Filas | Editar | `tier:update`: `label` y/o `color` |
| Filas | Subir/bajar | `tier:move`: intercambio con vecina; en el borde → sin cambio |
| Filas | Vaciar | `tier:clear`: items al final del banco; vacía → sin cambio |
| Filas | Eliminar | `tier:delete`: items al final del banco; última fila → "La tier list necesita al menos una fila." |
| Sala | Título | `title:update` |
| Bitácora | Registro | Entrada nueva al inicio, se muestran 100 (`MAX_ACTIVITY`). Textos exactos: `subió X`, `movió X a la fila S` / `a el banco` (solo si cambia de contenedor), `eliminó X`, `añadió una fila`, `vació la fila S`, `eliminó la fila S`. **No** se registra: reordenar en la misma fila, renombrar/recolorear fila, subir/bajar fila, título |
| Bitácora | Autor | `{ name: apodo, color: colorFor(apodo) }` (`hsl(h 60% 70%)`) |
| Común | "Sin cambio" | Si la acción no aplica, el reducer devuelve la misma sala: no se guarda, no se emite, no se registra |

Fuera de alcance porque la demo no lo tiene (no se agrega): cuentas/login, roles o dueño de sala,
borrar salas, lista de salas, presencia ("quién está conectado"), chat, votos, paginación de bitácora,
edición de nombre de imagen, moderación. La sala `DEMO42` **se queda en el frontend** (ver Decisiones).

## 2. Modelo de datos

**Decisión clave: el estado de la sala se guarda como un documento `jsonb`.** El estado ya es un
documento pequeño (`Room`) que se transmite completo en cada evento, y todas las reglas son
operaciones sobre listas ordenadas (insertar en índice, intercambiar). Guardarlo como documento
permite portar `roomReducer` tal cual (función pura en C#) y portar sus 9 pruebas 1:1. Tablas
relacionales Filas/Items obligarían a reescribir el orden con columnas `Posicion` sin ganar nada
que la demo use. `ponytail:` documento por sala; si algún día hay reportes cruzados entre salas,
normalizar.

| Tabla | Columnas | Restricciones |
|---|---|---|
| `Salas` | `Codigo char(6) PK`, `Estado jsonb NOT NULL` (`Room` completo: `{code, title, tiers, pool, items}`), `Version bigint NOT NULL DEFAULT 0`, `CreadaEn timestamptz` | CHECK `Codigo ~ '^[A-HJ-NP-Z2-9]{6}$'` |
| `Bitacora` | `Id bigint identity PK`, `SalaCodigo char(6)`, `Quien varchar(30)`, `Color varchar(24)`, `Texto varchar(120)`, `En timestamptz` | FK → `Salas` **CASCADE**; índice `(SalaCodigo, Id DESC)` |
| `Imagenes` | `Id uuid PK`, `SalaCodigo char(6)`, `Archivo varchar(80)`, `Bytes int`, `SubidaEn timestamptz` | FK → `Salas` **CASCADE**; CHECK `Bytes > 0` |

- `Version` sube en cada acción aplicada; viaja en el DTO para que el cliente descarte estados viejos.
- Serialización de escrituras por sala: `SELECT … FOR UPDATE` sobre la fila de `Salas` dentro de la
  transacción (dos personas arrastrando a la vez → se aplican en orden, ninguna se pierde).
- `Imagenes` registra cada archivo del volumen (para borrarlo con `item:delete` y detectar huérfanos).
- Ids: filas = string del cliente (validado, ver API); imágenes = `Guid` en formato `N` generado por el servidor.

## 3. Diseño de API

JSON camelCase. **Los nombres son los de `src/types.ts`** (`code, title, tiers[{id,label,color,items}], pool, items{id:{id,name,url,by}}`, `ActivityEntry{id,who{name,color},text,at}` con `at` en ms epoch) + `version`.
Errores HTTP como ProblemDetails con `detail` = mensaje de la demo; errores del hub como `HubException` con el mismo mensaje.

### HTTP

| Método y ruta | Responsabilidad | Entrada → Salida |
|---|---|---|
| `POST /api/salas` | Crear sala (`RoomService.create`) | `{ title }` → `201 Room` (+ `version`); título > 80 → 400 "El título admite máximo 80 caracteres." |
| `GET /api/salas/{codigo}` | Existe (`RoomService.exists`) | → `204` / `404` "No existe ninguna sala con el código X." |
| `POST /api/salas/{codigo}/imagenes` | Subir imagen + `item:add` + bitácora "subió X" + broadcast | multipart `archivo`, `nombre` (vacío → `'Imagen'`), `apodo` → `201 Item` (`id` = Guid `N`, `url` = `/imagenes/{codigo}/{id}.{ext}`) |
| `GET /imagenes/{codigo}/{archivo}` | Archivo estático del volumen | `Cache-Control: immutable`, con CORS (necesario para exportar PNG) |

Rate limit nativo por IP: crear 10/min, existe 30/min, subir 60/min → 429.

### Hub SignalR `/hubs/sala`

| Método (cliente → servidor) | Responsabilidad | Entrada → Salida |
|---|---|---|
| `Unirse(codigo, apodo)` | Valida sala, guarda apodo en la conexión, `Groups.AddToGroupAsync`, lee estado | → `{ room, activity }` (o `HubException` si no existe) |
| `Despachar(accion)` | Aplica acción con `FOR UPDATE`; si cambió: guarda, `Version++`, genera texto de bitácora, emite | `RoomAction` (sin `item:add`) → `void` |

| Evento (servidor → grupo) | Payload |
|---|---|
| `state` | `Room` (con `version`) |
| `activity` | `ActivityEntry[]` (últimas 100, igual que hoy) |

Acciones aceptadas: `item:move`, `item:delete`, `tier:update`, `tier:add`, `tier:move`, `tier:clear`,
`tier:delete`, `title:update` — mismo JSON que `RoomAction`, deserializado con
`[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]`.

**El cliente nunca manda lo que el servidor calcula:** el texto de bitácora y el color del autor los
genera el servidor desde la acción y el estado previo (el `text` que hoy pasa `act()` se ignora); el
`by` del item sale del apodo; el `url` lo arma el servidor. Validaciones en servidor: `MAX_TIERS`,
mínimo una fila, título ≤ 80, etiqueta ≤ 20, color `^#[0-9a-fA-F]{6}$`, id de fila nueva
`^[a-z0-9]{1,16}$` y no repetido, apodo 1–30, nombre ≤ 30, máx 200 imágenes por sala.

## 4. Arquitectura

```
rankeo-backend/
  Rankeo.Api/
    Program.cs              .env, DI, CORS, rate limit, ProblemDetails, SignalR, estáticos, migración al arrancar
    Data/RankeoDb.cs        DbContext (jsonb, CHECK, FK)
    Data/Migrations/
    Modelos/Entidades.cs    Sala, EntradaBitacora, Imagen
    Dominio/Room.cs         records Room/Tier/Item + RoomAction polimórfico (= src/types.ts)
    Dominio/RoomReducer.cs  port puro de roomReducer.ts
    Services/Calculos.cs    GenerarCodigo, NormalizarCodigo, ColorPara, TextoBitacora, NuevaSala
    Services/SalaService.cs crear, existe, cargar, aplicar(acción, apodo) en transacción
    Services/ImagenService.cs validar, guardar en disco, registrar, borrar
    Services/Errores.cs     ReglaException(400) / NoEncontradoException(404) + IExceptionHandler
    Hubs/SalaHub.cs         Unirse / Despachar → SalaService; emite state/activity
    Controllers/SalasController.cs
    Dockerfile .dockerignore
  Rankeo.Api.Tests/
    ApiFactory.cs           WebApplicationFactory + Testcontainers + HubConnection contra TestServer
  Rankeo.slnx  docker-compose.yml  .env.example  .gitignore  PLAN.md  CLAUDE.md
```

- **Controllers/Hub**: transporte. **Services**: reglas + EF Core directo. `RoomReducer` y `Calculos` puros.
- El hub emite **después** del commit (nunca estados no confirmados).
- Hora: `DateTimeOffset.UtcNow` (solo timestamps de bitácora; el cliente formatea). No hace falta `Reloj` de negocio: nada depende de "hoy".
- Sin auth: el código de sala es la llave (como hoy en la demo).
- Paquetes: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (misma versión de EF que trae Npgsql). Pruebas: `xunit`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.SignalR.Client`.
- **No** se usa: JWT/Identity, OpenAPI, repositorios, AutoMapper, MediatR, FluentValidation, ImageSharp (no se re-codifica la imagen; se valida por firma de bytes), Redis backplane (`ponytail:` una sola instancia de la API; si se escala a varias, `AddStackExchangeRedis`).

## 5. Infraestructura

`docker-compose.yml`:
- `db`: `postgres:17-alpine`, volumen `pgdata`, healthcheck `pg_isready`, publicado en `127.0.0.1:${DB_PORT:-5440}`.
- `api`: build `Rankeo.Api`, `depends_on: db (service_healthy)`, `${API_PORT:-8088}:8080`, volumen `imagenes:/data/imagenes`, `restart: unless-stopped`.

`.env.example`: `POSTGRES_PASSWORD` (obligatoria, `${VAR:?}`), `DB_PORT`, `API_PORT`,
`ConnectionStrings__Rankeo` (localhost para `dotnet run`; compose la arma con `Host=db`),
`CORS_ORIGINS` (lista separada por comas, p. ej. `http://localhost:5173`), `IMAGENES_DIR`
(`./data/imagenes` para `dotnet run`; compose fija `/data/imagenes`), `IMAGEN_MAX_KB` (default 512).

Al arrancar: `Database.Migrate()` y crear `IMAGENES_DIR` si falta. Sin semilla (la demo vive en el frontend).

## 6. Testing

- **Unitarias `RoomReducer`**: los 9 casos de `roomReducer.test.ts` portados con los mismos nombres y valores, + "no aplica → misma referencia" por cada acción.
- **Unitarias `Calculos`**: `ColorPara` con vectores calculados por `colorFor` de JS (incluye emoji: JS itera code points y toma `charCodeAt(0)` = surrogate alto); `NormalizarCodigo("  ab-c 12 3 ")` = `"ABC123"`; `GenerarCodigo` nunca contiene `0 O 1 I` ni es `DEMO42`; `TextoBitacora` para cada acción (incluye "a el banco" y "sin texto al reordenar en la misma fila").
- **Integración HTTP**: crear (título vacío → "Mi Tier List", 5 filas), existe 204/404, normalización del código en la ruta, subida: firma inválida → 400, demasiado grande → 400, ok → 201 + archivo en disco + `GET /imagenes/...` 200 con CORS.
- **Integración hub** (dos `HubConnection` en la misma sala): A mueve → B recibe `state` y `activity`; acción que no aplica → nadie recibe nada; `tier:delete` de la última → `HubException` con el mensaje; sala inexistente → `HubException`; **concurrencia**: A y B mandan 20 movimientos cada uno a la vez → estado final consistente y `version` = movimientos aplicados.
- `item:delete` borra el archivo del volumen y la fila de `Imagenes`.

## 7. Fases de implementación

| Fase | Tareas | Depende de | Criterio de aceptación |
|---|---|---|---|
| 0 Base | `Rankeo.slnx`, `Rankeo.Api`, `Rankeo.Api.Tests`, compose solo `db`, `.env.example`, lector de `.env`, `ApiFactory` con Testcontainers | — | `dotnet test` verde con 1 prueba que arranca la API contra Postgres real |
| 1 Dominio | `Dominio/Room.cs`, `RoomReducer.cs`, `Calculos.cs` + unitarias | 0 | 9 casos portados + Calculos en verde |
| 2 Datos | Entidades, `RankeoDb` (jsonb, CHECK, FK), migración inicial, `Migrate()` al arrancar | 1 | Migración aplica en base vacía; prueba de CHECK de código |
| 3 Salas HTTP | `SalasController` (crear, existe), errores ProblemDetails, CORS, rate limit | 2 | Pruebas HTTP de §6 en verde |
| 4 Hub | `SalaHub` + `SalaService.Aplicar` (`FOR UPDATE`, `Version`, bitácora, emitir tras commit) | 3 | Pruebas de hub de §6 incluida concurrencia |
| 5 Imágenes | `ImagenService`, endpoint multipart, estáticos con CORS + caché, borrado en `item:delete` | 4 | Pruebas de subida/borrado en verde |
| 6 Docker | `Dockerfile` multi-stage, compose completo con volumen `imagenes` | 5 | `docker compose -p rankeo-prueba up --build`: crear sala, subir imagen, `down`+`up` y sala e imagen siguen; `down -v` |
| 7 Frontend | Migración de §8 en rama del frontend | 6 | Dos navegadores en la misma sala ven cada cambio; demo `DEMO42` sigue igual; `npm test` y `npm run build` verdes |
| 8 Cierre | Prueba manual de punta a punta, README del backend, actualizar `/docs` del frontend (`sh docs/docs.sh stale`) | 7 | Verificación final completa |

## 8. Migración del frontend

Principio: las vistas no cambian; solo se cambia la implementación de `RoomService`.

1. `npm i @microsoft/signalr`; `VITE_API_URL` en `.env` (+ `.env.example`).
2. `src/room/signalrRoomService.ts`: implementa `RoomService`.
   - `exists` → `GET`; `create` → `POST`; `load` → arranca conexión (`withAutomaticReconnect`) + `invoke('Unirse', code, getName())`.
   - `dispatch` → **aplica `roomReducer` local de forma optimista** y emite `state` al instante; luego `invoke('Despachar', action)`. El `state` del servidor reemplaza al optimista. Evita el "rebote" del arrastre (`TierBoard` suelta su vista previa al soltar).
   - `subscribe` → handlers `state`/`activity`; descarta `state` con `version` menor a la actual (cubre la carrera subscribe/load de `useRoom`).
   - `onreconnected` → vuelve a `Unirse` (los grupos de SignalR no sobreviven a la reconexión). Errores del hub → evento `warning` (toast que ya existe).
   - URLs de imagen relativas (`/imagenes/...`) → prefijo `VITE_API_URL` en un solo punto.
3. `roomService` en `roomService.ts` pasa a ser compuesto: `DEMO42` → `LocalRoomService` (sin cambios), resto → SignalR.
4. Subida: agregar `addImage(code, dataUrl, name, by)` a `RoomService`. Local: `item:add` como hoy. SignalR: `fetch(dataUrl).blob()` → `POST` multipart. `addImages` en `RoomPage` llama a `roomService.addImage` en vez de `act`.
5. `tier:add`: se sigue mandando el id de `newId()` (8 chars base36, pasa la validación del servidor).
6. Exportar PNG con imágenes de otro origen: `crossOrigin="anonymous"` en el `<img>` de `ItemCard` y `useCORS: true` en `exportPng`.
7. Actualizar `rankeo-frontend/docs` (data-layer, state-management, deployment) y quitar el aviso de "sin backend".

## Decisiones propuestas (no vienen de la demo; confirmar cuando toque)

1. **`DEMO42` se queda 100% en el frontend** (`LocalRoomService`): cada visitante tiene su propia copia que no se guarda, igual que hoy. Una demo compartida en el servidor la podría vandalizar cualquiera.
2. Etiqueta de fila máx 20 chars (la demo no tiene límite).
3. Máx 200 imágenes por sala e imagen máx 512 KB (el cliente ya manda ~10–30 KB).
4. Formatos aceptados: webp, png, jpeg, gif (por firma de bytes). Se guarda tal cual, sin re-codificar.
5. Bitácora: se guardan todas las entradas, se leen las últimas 100. `ponytail:` sin purga; agregar si crece.
6. Salas abandonadas no se borran. `ponytail:` agregar limpieza por antigüedad si el volumen crece.
7. `title:update` sigue mandándose por tecla (como hoy). `ponytail:` debounce en el cliente si la carga molesta.
8. Imágenes huérfanas (subida OK pero falló el `item:add`): se borran en la misma operación con try/finally; sin job de limpieza.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Latencia: el arrastre "rebota" hasta que responde el servidor | Reducer optimista en el cliente (§8.2) |
| Estado viejo pisa uno nuevo (carrera subscribe/load, reconexión) | Campo `version`, el cliente ignora versiones menores |
| Escrituras simultáneas en la misma sala | `SELECT … FOR UPDATE` por sala; prueba de concurrencia |
| Exportar PNG falla con imágenes de otro origen (canvas "tainted") | CORS en `/imagenes` + `crossOrigin` + `useCORS` (§8.6) |
| CORS + SignalR: credenciales | Orígenes explícitos desde `CORS_ORIGINS` + `AllowCredentials()`; nunca `*` |
| Proxy inverso sin soporte de WebSocket | Documentar `Upgrade`/`Connection` en el README; SignalR cae a long polling solo |
| Rate limit detrás de proxy (todos con la misma IP) | `UseForwardedHeaders` cuando haya proxy (`ponytail:`) |
| Enumerar códigos por el hub (sin rate limit) | Espacio de 32⁶ ≈ 10⁹ códigos; `ponytail:` limitar `Unirse` por conexión si aparece abuso |
| `colorFor` distinto entre JS y C# con emoji | Vectores de prueba generados con el JS real |
| Discriminador `type` no va primero en el JSON | `AllowOutOfOrderMetadataProperties = true` en las opciones JSON del hub |
| Version de `EntityFrameworkCore.Design` ≠ la de Npgsql (CS1705) | Fijarlas juntas en Fase 0 |
| Puertos 5432/8080 ocupados | Resuelto: defaults 5440 (db) y 8088 (api), todo por variable |
| Push al frontend | Resuelto: remoto `yaxoff` (YaxOFF/rankeo-frontend-); trabajar en rama nueva |

## Verificación final

1. `dotnet test` verde; `docker compose up --build` limpio.
2. Frontend con `VITE_API_URL` apuntando a la API: crear sala en navegador A, unirse con el código en navegador B (otro perfil).
3. En A: subir 3 imágenes, moverlas a S y A, añadir fila, renombrarla, vaciarla, borrar una imagen a la papelera y otra con Supr → B ve cada cambio y la misma bitácora.
4. Mover en A y B a la vez → ambos terminan con el mismo estado.
5. Matar la API y levantarla → los clientes se reconectan y ven el estado guardado.
6. Descargar PNG en B con imágenes subidas desde A.
7. `/sala/DEMO42` funciona sin tocar la API.
8. `docker compose down` + `up` → salas e imágenes siguen.

## Avance

- [x] **Plan** — plan aprobado, `PLAN.md` y `CLAUDE.md` creados, repo git local creado (2026-10-24).
- [x] **Fase 0 — Base** (2026-10-24)
- [x] **Fase 1 — Dominio** (2026-10-24)
- [x] **Fase 2 — Datos** (2026-10-24)
- [x] **Fase 3 — Salas HTTP** (2026-10-24)
- [x] **Fase 4 — Hub SignalR** (2026-10-24)
- [x] **Fase 5 — Imágenes** (2026-10-24)
- [x] **Fase 6 — Docker** (2026-10-24)
- [x] **Fase 7 — Frontend** (2026-10-24)
- [x] **Fase 8 — Cierre** (2026-10-24)

### Bitácora

<!-- Una entrada por fase terminada: fecha, qué se hizo, archivos clave, decisiones tomadas, pendientes para la siguiente fase. -->

#### Fase 0 — Base (2026-10-24)

- Hecho: `Rankeo.slnx` con `Rankeo.Api` (web vacío, net10.0) y `Rankeo.Api.Tests` (xUnit). Compose solo con
  `db` (`postgres:17-alpine`, healthcheck, `127.0.0.1:${DB_PORT}`). `.env.example` con todas las variables del plan.
  Remoto `origin` = https://github.com/YaxOFF/rankeo-backend.
- Archivos clave: `Rankeo.Api/Program.cs` (función `CargarEnv`: lee el primer `.env` subiendo desde la carpeta
  actual, sin pisar variables ya definidas; falla al arrancar si falta `ConnectionStrings__Rankeo`),
  `Rankeo.Api.Tests/ApiFactory.cs` (`WebApplicationFactory<Program>` + `PostgreSqlContainer`, fixture por clase),
  `Rankeo.Api.Tests/ArranqueTests.cs` (1 prueba: arranca la API y abre conexión con su cadena).
- Versiones fijadas: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (trae EF Core 10.0.4) +
  `Microsoft.EntityFrameworkCore.Design` 10.0.4. Tests: `Microsoft.AspNetCore.Mvc.Testing` 10.0.13,
  `Testcontainers.PostgreSql` 4.16.0.
- Decisiones: la `ApiFactory` pasa la cadena del contenedor por variable de entorno (gana sobre el `.env`
  local porque `CargarEnv` no pisa). `Program` es público sin `partial class` (.NET 10 lo genera).
  Se borró `appsettings.Development.json` (vacío de valor).
- Puertos: en esta máquina 5432–5434, 5439, 8080, 8081, 8085, 8086 y 8090 están ocupados → defaults
  `DB_PORT=5440` y `API_PORT=8088` (compose, `.env.example` y `.env` local). Dentro de la red de Docker
  siguen 5432 (db) y 8080 (api).
- Pendientes: `Microsoft.AspNetCore.SignalR.Client` se agrega a las pruebas en Fase 4.

#### Fase 1 — Dominio (2026-10-24)

- Hecho: `Dominio/Room.cs` (records `Room/Tier/Item` con `IReadOnlyList`/`IReadOnlyDictionary`, constantes
  `Room.POOL` y `Room.MAX_TIERS`, `RoomAction` polimórfico con discriminador `type` y las 9 acciones, incluida
  `item:add`), `Dominio/RoomReducer.cs` (port 1:1 con `with`; devuelve la misma instancia si no aplica; también
  `ListOf` y `ContainerOf`), `Services/Calculos.cs` (`GenerarCodigo`, `NormalizarCodigo`, `NuevoId`, `ColorPara`,
  `NuevaSala`, `TextoBitacora`, `TIER_COLORS`).
- Pruebas: `RoomReducerTests.cs` (9 casos portados con los mismos nombres + "no aplica → misma referencia" por
  acción + tope de 15 filas, índice acotado, `tier:update` parcial), `CalculosTests.cs` (vectores de `colorFor`
  generados con Node, incluidos emoji y sustitutos sueltos). 34 en verde.
- Decisiones: `GenerarCodigo` usa `RandomNumberGenerator.GetString` (el código es la llave); no hace falta excluir
  `DEMO42` porque la "O" no está en el alfabeto. `NuevoId` = 8 chars base36 como `newId()`. `NuevaSala` usa el
  título por defecto solo si viene vacío (igual que `newRoom`; `Hero` ya hace `trim`). `TextoBitacora(antes, accion)`
  se calcula con el estado previo y devuelve `null` cuando no se registra; si la imagen no existe usa "la imagen"
  (como `itemName`). `Room` incluye `Code` (igual que `types.ts`); el jsonb de Fase 2 decide si se guarda.
- Pendientes: los mensajes "Máximo 15 filas." y "La tier list necesita al menos una fila." los lanza
  `SalaService` (Fase 4) como `ReglaException` antes de aplicar, porque el reducer solo devuelve "sin cambio".
  `AllowOutOfOrderMetadataProperties` en las opciones JSON del hub (Fase 4).

#### Fase 2 — Datos (2026-10-24)

- Hecho: `Modelos/Entidades.cs` (`Sala`, `EntradaBitacora`, `Imagen`), `Data/RankeoDb.cs` (tablas `Salas`,
  `Bitacora`, `Imagenes`; `char(6)`, CHECK de código y de `Bytes`, FK CASCADE, índice `(SalaCodigo, Id DESC)`),
  migración `Data/Migrations/*_Inicial.cs`, `AddDbContext` + `Database.Migrate()` al arrancar en `Program.cs`.
- Pruebas: `DatosTests.cs` (migración sin pendientes, CHECK rechaza `ABC10O`/`abcdef`/`ABCDE`, jsonb camelCase
  ida y vuelta + consulta `Estado->>'title'`, CASCADE de bitácora). 39 en verde.
- Decisiones: `Sala.Estado` es `Room` con `HasConversion` a JSON (`JsonSerializerOptions.Web`) en columna `jsonb`;
  se guarda el `Room` completo, incluido `code` (duplicado inofensivo, evita otro record). Sin `ValueComparer`:
  `Room` es inmutable y el reducer devuelve otra instancia, así que la comparación por referencia detecta el cambio.
  Pruebas sin paralelismo entre clases (`DisableTestParallelization` en `ApiFactory.cs`): cada fixture pasa su
  cadena por variable de entorno del proceso y en paralelo se pisaban.
- Pendientes: ninguno para Fase 3.

#### Fase 3 — Salas HTTP (2026-10-24)

- Hecho: `Controllers/SalasController.cs` (`POST /api/salas` → 201 con `Room` + `version` y `Location`;
  `GET /api/salas/{codigo}` → 204/404), `Services/SalaService.cs` (`Crear`, `Existe`, `Dto`), `Services/Errores.cs`
  (`ReglaException`, `NoEncontradoException`, `ErroresHandler` → ProblemDetails con `detail` = mensaje).
  `Program.cs`: controllers, ProblemDetails, CORS desde `CORS_ORIGINS` con `AllowCredentials`, rate limit por IP
  (`crear` 10/min, `existe` 30/min, ventana fija, 429).
- Pruebas: `SalasHttpTests.cs` (título vacío → "Mi Tier List" + 5 filas con colores, título guardado, título > 80 → 400
  problem+json, 204 con código normalizado en la ruta, 404 con mensaje de la demo, CORS permite solo el origen
  configurado con credenciales, 11.ª creación desde la misma IP → 429). 46 en verde.
- Decisiones: mensaje nuevo "El título admite máximo 80 caracteres." (la demo solo tiene `maxLength`). El código se
  genera y se reintenta si ya existe. `SalaService.Dto(sala)` es el payload de estado (lo reutiliza el hub en Fase 4).
  La política `subir` (60/min) se agrega en Fase 5 con su endpoint. La `ApiFactory` fija `CORS_ORIGINS=http://front.test`.
  Las pruebas HTTP comparten la IP vacía del TestServer: mantener ≤ 10 creaciones por clase o usar
  `Server.SendAsync` con otra `RemoteIpAddress`.
- Pendientes: ninguno para Fase 4.

#### Fase 4 — Hub SignalR (2026-10-24)

- Hecho: `Hubs/SalaHub.cs` (`Unirse(codigo, apodo)` → `{ room, activity }`, guarda sala y apodo en `Context.Items`,
  cambia de grupo si ya estaba en otra sala; `Despachar(accion)` → emite `state` al grupo y `activity` solo si la
  acción dejó bitácora, siempre después del commit). `SalaService`: `Cargar`, `Actividad` (últimas 100 como
  `ActivityEntry`, `id` = Id de la fila como string, `at` en ms) y `Aplicar(codigo, accion, apodo)` (transacción +
  `SELECT … FOR UPDATE` vía `FromSql(...).ToListAsync()` sin componer, validaciones, reducer, `Version++`, bitácora).
  `Program.cs`: `AddSignalR().AddJsonProtocol` con `AllowOutOfOrderMetadataProperties`, `RespectNullableAnnotations`
  y `RespectRequiredConstructorParameters` (un campo obligatorio que falta o llega null se rechaza al deserializar),
  `MapHub<SalaHub>("/hubs/sala")`.
- Pruebas: `SalaHubTests.cs` (unirse con código en minúsculas devuelve sala + bitácora con autor y color; sala
  inexistente / apodo vacío / despachar sin unirse → `HubException`; A mueve → B recibe `state` y `activity`
  con "movió Gato a la fila S", con `type` al final del JSON; acción que no aplica no emite y `tier:update` emite
  `state` sin `activity`; reglas → `HubException`; 20+20 movimientos simultáneos → `version` y bitácora = 42).
  `ApiFactory.Hub()` conecta por long polling al TestServer. 52 en verde.
- Decisiones: mensajes nuevos (la demo no los tiene): "El apodo debe tener entre 1 y 30 caracteres.",
  "Primero únete a una sala.", "Las imágenes se suben por HTTP.", "Id de fila inválido.", "Color inválido.",
  "La etiqueta admite máximo 20 caracteres.". "La tier list necesita al menos una fila." se lanza con 1 fila aunque
  el id no exista (igual que `onDelete` de la demo). `Aplicar` sí acepta `item:add`: lo bloquea el hub, y la Fase 5
  lo llama desde el endpoint de subida. `Aplicar` devuelve `null` si no aplica (nada se guarda ni se emite).
  Con escrituras concurrentes, los `state` pueden llegar fuera de orden: el cliente descarta por `version` (§8.2).
- Pendientes (Fase 5): el endpoint de subida debe llamar `Aplicar(codigo, new ItemAdd(...), apodo)` y emitir
  `state`/`activity` con `IHubContext<SalaHub>`; `item:delete` debe borrar archivo y fila de `Imagenes` (hoy solo
  quita el item del estado). Límite de 200 imágenes por sala va en esa fase.

#### Fase 5 — Imágenes (2026-10-24)

- Hecho: `Services/ImagenService.cs` (`Subir`: valida apodo/nombre/tamaño/firma, comprueba la sala, escribe
  `IMAGENES_DIR/{codigo}/{guid}.{ext}`, agrega la fila `Imagenes` y llama `Aplicar(ItemAdd)`; si algo falla borra el
  archivo). `POST /api/salas/{codigo}/imagenes` en `SalasController` (rate limit `subir` 60/min; emite `state` y
  `activity` con `IHubContext<SalaHub>` tras el commit). `Aplicar` con `item:delete` borra la fila de `Imagenes` en la
  misma transacción y el archivo después del commit; tope de 200 imágenes en `Validar` (bajo el `FOR UPDATE`).
  `Program.cs`: `UseStaticFiles` en `/imagenes` sobre `IMAGENES_DIR` (se crea al arrancar), después de `UseCors`,
  con `Cache-Control: public, max-age=31536000, immutable`.
- Pruebas: `ImagenesTests.cs` (subida OK → 201, archivo en disco, `state`/`activity` "subió Gato" al grupo, GET con CORS,
  `image/png` e `immutable`; nombre vacío → "Imagen"; firma inválida, > 512 KB, nombre > 30, apodo vacío → 400;
  sala inexistente → 404 sin carpeta; tope 200 → 400 sin archivo ni fila huérfanos; `item:delete` borra archivo y fila).
  `ApiFactory` usa una carpeta temporal por instancia como `IMAGENES_DIR`. 57 en verde.
- Decisiones: mensajes nuevos "El nombre admite máximo 30 caracteres.", "Falta el archivo.",
  "La imagen admite máximo {IMAGEN_MAX_KB} KB.", "Formato no admitido: usa webp, png, jpeg o gif.",
  "Máximo 200 imágenes por sala.". La validación del apodo pasó a `SalaService.Apodo` (la usan hub y subida).
  La fila `Imagenes` se agrega al contexto antes de `Aplicar` y se guarda en su transacción (mismo `DbContext` scoped).
  El `id` del item es el Guid de la imagen (32 chars), así `item:delete` encuentra el archivo.
- Pendientes (Fase 6): el volumen `imagenes:/data/imagenes` en compose; en local con `dotnet run`, `IMAGENES_DIR`
  del `.env` (`/data/imagenes`) cae en la raíz de la unidad actual: usar p. ej. `./data/imagenes` (ignorado por git).


#### Fase 6 — Docker (2026-10-24)

- Hecho: `Rankeo.Api/Dockerfile` multi-stage (`sdk:10.0` → `aspnet:10.0`, restore en capa propia, corre como el
  usuario `app` no root, crea `/data/imagenes` con dueño `app` para que el volumen con nombre herede el permiso),
  `Rankeo.Api/.dockerignore`. `docker-compose.yml` con servicio `api` (build `Rankeo.Api`, `depends_on db healthy`,
  `${API_PORT:-8088}:8080`, volumen `imagenes:/data/imagenes`, `restart: unless-stopped`).
- Verificado: `DB_PORT=5441 API_PORT=8089 docker compose -p rankeo-prueba up --build` → crear sala, subir PNG
  (201), `down` + `up` → sala 204, imagen 200, fila con `Version` 1 y 1 item en el banco; `down -v`. 57 pruebas en verde.
- Decisiones: compose pasa variables explícitas al `api` (no `env_file`): arma `ConnectionStrings__Rankeo` con
  `Host=db` y `POSTGRES_PASSWORD`, y fija `IMAGENES_DIR=/data/imagenes`; `CORS_ORIGINS` e `IMAGEN_MAX_KB` salen del
  `.env`. En el contenedor no hay `.env` (`CargarEnv` no encuentra nada y no pasa nada). `IMAGENES_DIR` del
  `.env`/`.env.example` pasó a `./data/imagenes` (para `dotnet run`, ignorado por git). La API se publica en todas las
  interfaces (el frontend va en otro contenedor/origen); la base solo en `127.0.0.1`.
- Pendientes (Fase 7): el frontend apunta `VITE_API_URL` a `http://localhost:8088` (compose) y `CORS_ORIGINS` debe
  incluir su origen (`http://localhost:5173` por defecto).

#### Fase 7 — Frontend (2026-10-24)

- Hecho (rama `feat/backend-signalr` del frontend, push a `yaxoff`, sale de `docs/documentacion-inicial`):
  `src/room/signalrRoomService.ts` (`SignalRRoomService`: `exists`/`create` por HTTP, `load` = conexión +
  `Unirse`, `dispatch` optimista con `roomReducer` y vuelta al último estado confirmado si el hub lo rechaza,
  descarte de `state` con `version` menor, `onreconnected` → `Unirse` otra vez, `withApi` antepone
  `VITE_API_URL` a `/imagenes/...`). `roomService` pasó a ser compuesto (`DEMO42` → `LocalRoomService`, resto →
  SignalR; `create` siempre a la API). `RoomService.addImage` nuevo (local: `item:add` + "subió X"; SignalR:
  multipart). `RoomPage.addImages` usa `addImage` y muestra el `detail` del servidor como toast.
  `crossOrigin="anonymous"` en `ItemCard` + `useCORS` en `exportPng`. `.env.example` con `VITE_API_URL`.
  Docs del frontend, README y CLAUDE.md actualizados (sin el aviso de "sin backend").
- Verificado: `npm test` (9), `npm run build`, `npm run lint` verdes; `dotnet test` 57 en verde. Prueba manual con
  compose + Vite en dos pestañas: crear sala, subir 2 imágenes, añadir fila, mover con teclado en B → A lo ve con
  la misma bitácora, Supr en A → B lo ve, PNG en B sin error de canvas, `DEMO42` funciona, reinicio de la API →
  A se reconecta y sigue despachando.
- Decisiones: el texto de bitácora que pasa `act()` se ignora en SignalR (lo genera el servidor). Errores de regla
  en `Unirse` → `load` devuelve `null` ("no existe"); error de red → `useRoom` muestra "No se pudo conectar con la
  sala." y `JoinModal` muestra el error en vez de quedarse "Buscando…". Una sola conexión por pestaña (no se
  cierra al salir de la sala). `ponytail:` estado optimista simple: con dos acciones propias en vuelo, el `state`
  de la primera puede deshacer un instante la segunda hasta que llega el suyo.
- Pendientes (Fase 8): (`feat/backend-signalr` ya está en `main` del frontend, fast-forward 0ed1e18) `sh docs/docs.sh stale`
  y sellos `rev` de los docs tocados; README del backend; verificación final con dos perfiles reales.

#### Fase 8 — Cierre (2026-10-24)

- Hecho: `README.md` del backend (arranque, variables, API, pruebas, proxy inverso con WebSocket). Docs del
  frontend en la rama `docs/cierre-backend` (push a `yaxoff`, sale de `main` 0ed1e18): `folder-structure`
  (`signalrRoomService.ts`), `page-components` (`addImage`), `custom-hooks` y `routing` (errores de conexión),
  `helpers` (`useCORS`), y sellos `rev` de todos los docs a 0ed1e18; `sh docs/docs.sh stale` limpio.
- Verificado: `dotnet test` 57, `npm test` 9, `npm run build` y `npm run lint` verdes. Prueba de punta a punta
  con script (dos clientes SignalR reales contra `docker compose -p rankeo-prueba`): subir 3 imágenes, mover a S y
  A, añadir fila, renombrar, vaciar, borrar → B ve cada cambio y la misma bitácora; imagen con CORS para el PNG;
  15+15 movimientos simultáneos → A y B con el mismo estado (`version` 39); `down` + `up` → sala, imagen y
  `version` siguen; `down -v`. La parte de navegador (dos pestañas, reconexión, PNG, `DEMO42`) se probó en Fase 7.
- Decisiones: ninguna nueva.
- Pendientes: ninguno (ver la prueba en navegador abajo).
- Prueba en navegador (mismo día): `docs/cierre-backend` fusionada en `main` del frontend. Frontend en contenedor
  (`Dockerfile` + `nginx.conf` nuevos en el frontend, `:5174`) + compose del backend con
  `CORS_ORIGINS=http://localhost:5173,http://localhost:5174,http://127.0.0.1:5174`. Dos pestañas de Chrome con
  orígenes distintos (`localhost` y `127.0.0.1`, así cada una tiene su propio apodo). Probado con UI real y scripts
  en la página: subir, mover con teclado, Supr, añadir/renombrar/vaciar/eliminar fila, tope 15 filas, etiqueta > 20
  (toast), 25+25 movimientos de fila simultáneos desde las dos pestañas → mismo estado, PNG desde el otro origen
  sin canvas "tainted", cambiar de sala en la misma pestaña (aislado y vuelve a recibir), sala inexistente,
  `DEMO42` sin tocar la API, imagen corrupta (toast), reinicio corto de la API.
- Bugs encontrados y corregidos (frontend `fffc3e2`): (1) con la API caída > ~40 s, `withAutomaticReconnect` se
  rendía y la pestaña quedaba "En línea" con todos los cambios fallando hasta recargar → reintentos sin fin (tope
  10 s); (2) durante la caída seguía diciendo "En línea" → evento `online` en `RoomEvent`, TopBar muestra
  "Conectando…"; (3) si la API estaba caída al abrir la sala se quedaba en "Conectando con la sala…" para siempre
  → `useRoom` reintenta `load` cada 3 s.
- Notas: la etiqueta puede verse con más de 20 caracteres mientras se escribe (el servidor rechaza, toast, al
  salir vuelve al valor guardado); la demo no tenía límite, se deja así.
