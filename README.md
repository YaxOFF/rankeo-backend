# Rankeo API

Backend de [Rankeo](https://github.com/YaxOFF/rankeo-frontend-) (tier lists en grupo): salas compartidas en
vivo. Todos los que abren el mismo código ven cada cambio al momento.

.NET 10 · PostgreSQL 17 · SignalR · EF Core. Sin login: el código de sala es la llave.

## Arrancar

Requisitos: .NET SDK 10, Docker con Compose.

```bash
cp .env.example .env          # cambia POSTGRES_PASSWORD (y la misma en ConnectionStrings__Rankeo)
docker compose up --build     # db + api en http://localhost:8088
```

Desarrollo sin contenedor para la API:

```bash
docker compose up -d db
dotnet run --project Rankeo.Api
```

La base se migra sola al arrancar. Las imágenes van al volumen `imagenes` (compose) o a `./data/imagenes`
(`dotnet run`).

## Variables (`.env`)

| Variable | Default | Para qué |
|---|---|---|
| `POSTGRES_PASSWORD` | — (obligatoria) | Contraseña de Postgres |
| `DB_PORT` | `5440` | Puerto de la base en `127.0.0.1` |
| `API_PORT` | `8088` | Puerto publicado de la API |
| `ConnectionStrings__Rankeo` | — | Solo para `dotnet run`; compose la arma con `Host=db` |
| `CORS_ORIGINS` | `http://localhost:5173` | Orígenes del frontend, separados por comas (nunca `*`) |
| `IMAGENES_DIR` | `./data/imagenes` | Solo `dotnet run`; en compose es `/data/imagenes` |
| `IMAGEN_MAX_KB` | `512` | Tamaño máximo por imagen |

El frontend necesita `VITE_API_URL` apuntando a esta API, y su origen debe estar en `CORS_ORIGINS`.

## API

HTTP (errores como ProblemDetails, el mensaje en `detail`):

| Método y ruta | Qué hace |
|---|---|
| `POST /api/salas` | Crea sala `{ title }` → `201 Room` |
| `GET /api/salas/{codigo}` | `204` si existe, `404` si no |
| `POST /api/salas/{codigo}/imagenes` | Multipart `archivo`, `nombre`, `apodo`, `subtitulo` (opcional, ≤ 80) → `201 Item` (webp/png/jpeg/gif) |
| `POST /api/salas/{codigo}/derivadas` | Sub-tier list `{ title?, apodo? }` → `201 Room` nueva con las mismas filas (vacías) e imágenes (copiadas, todas en el banco), `parent` = código origen |
| `GET /imagenes/{codigo}/{archivo}` | Imagen estática, con CORS y caché `immutable` |

Rate limit por IP: crear 10/min (también derivadas), existe 30/min, subir 60/min.

SignalR en `/hubs/sala`:

- `Unirse(codigo, apodo)` → `{ room, activity }`.
- `Despachar(accion)` con un `RoomAction` (`item:move`, `item:delete`, `tier:*`, `title:update`).
- `Cursor(x, y, dragId, away)`: tu puntero (px relativos al tablero) y la imagen que arrastras; se reenvía a los demás sin guardarse.
- Eventos al grupo: `state` (`Room` + `version`), `activity` (últimas 100 entradas), `presence` (conectados: `{ id, name, color }[]`, al entrar/salir) y `cursor` (`{ id, name, color, x, y, dragId, away }`, solo a los demás).

El servidor calcula el texto y color de la bitácora, el autor y la URL; el cliente nunca los manda.

## Pruebas

```bash
dotnet test   # requiere Docker encendido (Testcontainers con Postgres real)
```

## Proxy inverso

SignalR usa WebSocket: el proxy debe pasar `Upgrade` y `Connection`. Ejemplo nginx:

```nginx
location /hubs/ {
    proxy_pass http://api:8080;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
}
```

Sin eso funciona igual, pero cae a long polling. Detrás de un proxy, el rate limit ve una sola IP
para todos.

## Más

Plan, decisiones y bitácora de cada fase: [`PLAN.md`](PLAN.md).
