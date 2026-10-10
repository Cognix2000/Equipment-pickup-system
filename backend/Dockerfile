# API e migrador na mesma imagem: `dotnet Retirada.Api.dll` sobe a API;
# `dotnet Retirada.Api.dll migrar` aplica as migrações e sai.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Retirada.slnx ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore src/Retirada.Api/Retirada.Api.csproj
RUN dotnet publish src/Retirada.Api/Retirada.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# Chaves da Data Protection (cookie e segredo do 2FA): volume persistente, gravável pelo usuário sem privilégio.
RUN mkdir /chaves && chown $APP_UID /chaves
ENV ASPNETCORE_URLS=http://+:8080 \
    DataProtection__Pasta=/chaves
EXPOSE 8080
# Usuário sem privilégio que a imagem oficial já traz.
USER $APP_UID
ENTRYPOINT ["dotnet", "Retirada.Api.dll"]
