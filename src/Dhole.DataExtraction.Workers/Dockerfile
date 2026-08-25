# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
  && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
  && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG GITHUB_ACTOR=github-actions
WORKDIR /src

COPY ["NuGet.Config", "./NuGet.Config"]
COPY ["src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj", "src/Dhole.DataExtraction.Workers/"]
COPY ["src/Dhole.DataExtraction.Application/Dhole.DataExtraction.Application.csproj", "src/Dhole.DataExtraction.Application/"]
COPY ["src/Dhole.DataExtraction.Contracts/Dhole.DataExtraction.Contracts.csproj", "src/Dhole.DataExtraction.Contracts/"]
COPY ["src/Dhole.DataExtraction.Domain/Dhole.DataExtraction.Domain.csproj", "src/Dhole.DataExtraction.Domain/"]
COPY ["src/Dhole.DataExtraction.Infrastructure/Dhole.DataExtraction.Infrastructure.csproj", "src/Dhole.DataExtraction.Infrastructure/"]
COPY ["src/Dhole.DataExtraction.Persistence/Dhole.DataExtraction.Persistence.csproj", "src/Dhole.DataExtraction.Persistence/"]

RUN --mount=type=secret,id=github_token,required=true \
  export NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_token);ValidAuthenticationTypes=Basic" && \
  dotnet restore "src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj" --configfile /src/NuGet.Config

COPY . .

RUN dotnet publish "src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj" \
  -c Release \
  -o /app/publish \
  --no-restore \
  /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Dhole.DataExtraction.Workers.dll"]
