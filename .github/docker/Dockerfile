# syntax=docker/dockerfile:1.7

# API and Workers share the same runtime dependencies and compile-time restore.
# BuildKit/bake deduplicates these stages when both targets are built together.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 poppler-utils tesseract-ocr tesseract-ocr-eng tesseract-ocr-spa \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-base
ARG GITHUB_ACTOR=github-actions
WORKDIR /src
COPY ["NuGet.Config", "./NuGet.Config"]
COPY ["src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj", "src/Dhole.DataExtraction.Api/"]
COPY ["src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj", "src/Dhole.DataExtraction.Workers/"]
COPY ["src/Dhole.DataExtraction.Application/Dhole.DataExtraction.Application.csproj", "src/Dhole.DataExtraction.Application/"]
COPY ["src/Dhole.DataExtraction.Contracts/Dhole.DataExtraction.Contracts.csproj", "src/Dhole.DataExtraction.Contracts/"]
COPY ["src/Dhole.DataExtraction.Domain/Dhole.DataExtraction.Domain.csproj", "src/Dhole.DataExtraction.Domain/"]
COPY ["src/Dhole.DataExtraction.Infrastructure/Dhole.DataExtraction.Infrastructure.csproj", "src/Dhole.DataExtraction.Infrastructure/"]
COPY ["src/Dhole.DataExtraction.Persistence/Dhole.DataExtraction.Persistence.csproj", "src/Dhole.DataExtraction.Persistence/"]

# A single shared restore downloads common projects just once.
# The GitHub credential is secret-mounted and is never persisted in the image.
RUN --mount=type=secret,id=github_token,required=true \
    --mount=type=cache,id=dhole-dataextraction-nuget,target=/root/.nuget/packages,sharing=locked \
    export NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_token);ValidAuthenticationTypes=Basic" \
    && dotnet restore src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj --configfile NuGet.Config \
    && dotnet restore src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj --configfile NuGet.Config

COPY . .

FROM build-base AS api-publish
RUN --mount=type=cache,id=dhole-dataextraction-nuget,target=/root/.nuget/packages \
    dotnet publish src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM build-base AS workers-publish
RUN --mount=type=cache,id=dhole-dataextraction-nuget,target=/root/.nuget/packages \
    dotnet publish src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM runtime AS api
EXPOSE 5205 5306
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production
COPY --from=api-publish /app/publish .
ENTRYPOINT ["dotnet", "Dhole.DataExtraction.Api.dll"]

FROM runtime AS workers
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production
COPY --from=workers-publish /app/publish .
ENTRYPOINT ["dotnet", "Dhole.DataExtraction.Workers.dll"]
