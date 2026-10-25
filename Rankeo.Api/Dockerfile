FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Rankeo.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# El volumen con nombre copia este dueño la primera vez: el usuario app (no root) puede escribir.
RUN mkdir -p /data/imagenes && chown app:app /data/imagenes
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rankeo.Api.dll"]
