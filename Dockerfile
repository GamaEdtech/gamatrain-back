# The API image used by docker-compose.yml. Publishes the solution the same way the deploy workflows do
# (.github/workflows/vps-deploy-dotnet.yml: `dotnet publish --configuration Release -o app` from src/).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish --configuration Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Shared libraries chrome-headless-shell needs: PuppeteerSharp downloads it on the first exam export
# (HeadlessBrowserRenderProvider), into HeadlessBrowser:DownloadPath.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libasound2t64 libatk-bridge2.0-0t64 libatk1.0-0t64 libcups2t64 libdbus-1-3 libdrm2 libgbm1 \
        libnspr4 libnss3 libxcomposite1 libxdamage1 libxfixes3 libxkbcommon0 libxrandr2 \
        libpango-1.0-0 libcairo2 fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Folders the app writes to at runtime; compose mounts volumes on them, which inherit this ownership.
RUN mkdir -p wwwroot/Files wwwroot/sitemap logs /home/app/chrome \
    && chown -R app:app wwwroot/Files wwwroot/sitemap logs /home/app/chrome
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    HeadlessBrowser__DownloadPath=/home/app/chrome
EXPOSE 8080
ENTRYPOINT ["dotnet", "GamaEdtech.Presentation.Api.dll"]
